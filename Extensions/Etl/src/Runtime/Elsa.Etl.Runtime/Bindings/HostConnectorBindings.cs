using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Composes typed approvals without exposing HTTP aliases to the v1 file catalog.</summary>
internal sealed class HostConnectorBindings(HostFileBindings files, HostHttpBindings http, int maxPartitions, HostSqlBindings? sqlBindings = null,
    HostRabbitMqBindings? rabbitMqBindings = null)
    : IEtlConnectorBindingCatalog, IEtlConnectorBindingResolver
{
    private readonly HostSqlBindings sql = sqlBindings ?? new HostSqlBindings([], []);
    private readonly HostRabbitMqBindings rabbitMq = rabbitMqBindings ?? new HostRabbitMqBindings([], []);

    public IReadOnlyList<EtlConnectorBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take)
    {
        var fileEntries = files.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, FileKind(x.Role)));
        var httpEntries = http.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, EtlConnectorKind.Http));
        var sqlEntries = sql.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, EtlConnectorKind.Sql));
        var rabbitMqEntries = rabbitMq.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, EtlConnectorKind.RabbitMq));
        var entries = fileEntries.Concat(httpEntries).Concat(sqlEntries).Concat(rabbitMqEntries).ToArray();
        if (entries.GroupBy(x => (x.Alias, x.Role)).Any(group => group.Count() != 1))
            throw Ambiguous();
        return entries.OrderBy(x => x.Alias, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal).Take(take).ToArray();
    }

    public async ValueTask<EtlConnectorBindingResolution> ResolveAsync(EtlScope scope, EtlPipelineV2 pipeline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(pipeline);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<string> shards = Array.Empty<string>();
            EtlConnectorBindingRevision source;
            if (pipeline.Source is EtlCsvSource csv)
                source = File(scope, csv.Binding, EtlFileRole.Source);
            else if (pipeline.Source is EtlJsonlSource jsonl)
                // File approvals are format-neutral; the pin records the declared input format.
                source = File(scope, jsonl.Binding, EtlFileRole.Source) with { Kind = EtlConnectorKind.Jsonl };
            else if (pipeline.Source is EtlHttpJsonSource json)
            {
                CheckUnambiguous(scope, json.Binding, "Source");
                await using var binding = await http.ResolveCurrentAsync(scope, json.Binding, "Source", cancellationToken);
                shards = Array.AsReadOnly(binding.Shards.Select(x => x.Id).ToArray());
                if (shards.Count > pipeline.Limits.MaxPartitions || shards.Count > maxPartitions)
                    throw new EtlBindingException("etl.http-shard-limit", "The HTTP source exceeds the permitted partition count.");
                source = Http(binding);
            }
            else if (pipeline.Source is EtlSqlSource table)
            {
                // A SQL source's content is unknown until preparation reads it, so its pin carries no digest (spec 1198).
                CheckUnambiguous(scope, table.Binding, "Source");
                var resolved = sql.ResolveCurrent(scope, table.Binding, "Source", table.Table);
                source = new(EtlConnectorKind.Sql, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null));
            }
            else if (pipeline.Source is EtlRabbitMqSource queue)
            {
                // A queue's messages are unknown until preparation receives them, so its pin carries no digest (spec 1201).
                CheckUnambiguous(scope, queue.Binding, "Source");
                var resolved = rabbitMq.ResolveCurrent(scope, queue.Binding, "Source", queue.Queue);
                source = new(EtlConnectorKind.RabbitMq, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null));
            }
            else
                throw Unavailable();
            var lookup = pipeline.Lookup is { } lookupDefinition ? File(scope, lookupDefinition.Binding, EtlFileRole.Lookup) : null;
            var main = await DestinationAsync(scope, pipeline.Routes.Main, EtlFileRole.MainOutput, cancellationToken);
            var errors = await DestinationAsync(scope, pipeline.Routes.Errors, EtlFileRole.ErrorOutput, cancellationToken);
            return new(new EtlConnectorRunBindings(source, lookup, main, errors, shards), null);
        }
        catch (EtlBindingException exception)
        {
            return new(null, exception.Code);
        }
    }

    private EtlConnectorBindingRevision File(EtlScope scope, string alias, EtlFileRole role)
    {
        CheckUnambiguous(scope, alias, role.ToString());
        return new(FileKind(role.ToString()), EtlRuntimeMetadata.Binding(files.Resolve(scope, alias, role)));
    }

    private async ValueTask<EtlConnectorBindingRevision> DestinationAsync(EtlScope scope, EtlDestination destination,
        EtlFileRole role, CancellationToken cancellationToken)
    {
        if (destination is EtlJsonlDestination file)
            return File(scope, file.Binding, role);
        if (destination is EtlCsvDestination csv)
            // Output folder approvals are format-neutral; the pin records the declared output format.
            return File(scope, csv.Binding, role) with { Kind = EtlConnectorKind.Csv };
        if (destination is EtlSqlDestination table)
        {
            CheckUnambiguous(scope, table.Binding, role.ToString());
            var resolved = sql.ResolveCurrent(scope, table.Binding, role.ToString(), table.Table);
            return new(EtlConnectorKind.Sql, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null));
        }
        if (destination is EtlRabbitMqDestination exchange)
        {
            CheckUnambiguous(scope, exchange.Binding, role.ToString());
            var resolved = rabbitMq.ResolveCurrent(scope, exchange.Binding, role.ToString(), exchange.Exchange);
            return new(EtlConnectorKind.RabbitMq, new EtlBindingRevision(resolved.Alias, resolved.Revision, resolved.Handle, null));
        }
        if (destination is not EtlHttpDestination endpoint)
            throw Unavailable();
        CheckUnambiguous(scope, endpoint.Binding, role.ToString());
        await using var binding = await http.ResolveCurrentAsync(scope, endpoint.Binding, role.ToString(), cancellationToken);
        return Http(binding);
    }

    private void CheckUnambiguous(EtlScope scope, string alias, string role)
    {
        var catalogs = (files.ContainsBinding(scope, alias, role) ? 1 : 0) + (http.ContainsBinding(scope, alias, role) ? 1 : 0) +
            (sql.ContainsBinding(scope, alias, role) ? 1 : 0) + (rabbitMq.ContainsBinding(scope, alias, role) ? 1 : 0);
        if (catalogs > 1)
            throw Ambiguous();
    }

    private static EtlConnectorKind FileKind(string role) => role is "Source" or "Lookup" ? EtlConnectorKind.Csv : EtlConnectorKind.Jsonl;
    private static EtlConnectorBindingRevision Http(HostHttpResolvedBinding binding) =>
        new(EtlConnectorKind.Http, new EtlBindingRevision(binding.Alias, binding.Revision, binding.Handle, null));
    private static EtlBindingException Ambiguous() => new("etl.binding-ambiguous", "An ETL alias has ambiguous connector approval for its role.");
    private static EtlBindingException Unavailable() => new("etl.binding-unavailable", "The scoped ETL binding is unavailable for this connector role.");
}
