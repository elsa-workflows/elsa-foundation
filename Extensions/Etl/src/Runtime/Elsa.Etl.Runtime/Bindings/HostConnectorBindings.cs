using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Composes typed approvals without exposing HTTP aliases to the v1 file catalog.</summary>
internal sealed class HostConnectorBindings(HostFileBindings files, HostHttpBindings http, int maxPartitions)
    : IEtlConnectorBindingCatalog, IEtlConnectorBindingResolver
{
    public IReadOnlyList<EtlConnectorBindingSummary> ListBindings(EtlScope scope, string? afterAlias, string? afterRole, int take)
    {
        var fileEntries = files.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, FileKind(x.Role)));
        var httpEntries = http.ListBindings(scope, afterAlias, afterRole, take)
            .Select(x => new EtlConnectorBindingSummary(x.Alias, x.Role, EtlConnectorKind.Http));
        var entries = fileEntries.Concat(httpEntries).ToArray();
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
            else if (pipeline.Source is EtlHttpJsonSource json)
            {
                CheckUnambiguous(scope, json.Binding, "Source");
                await using var binding = await http.ResolveCurrentAsync(scope, json.Binding, "Source", cancellationToken);
                shards = Array.AsReadOnly(binding.Shards.Select(x => x.Id).ToArray());
                if (shards.Count > pipeline.Limits.MaxPartitions || shards.Count > maxPartitions)
                    throw new EtlBindingException("etl.http-shard-limit", "The HTTP source exceeds the permitted partition count.");
                source = Http(binding);
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
        if (destination is not EtlHttpDestination endpoint)
            throw Unavailable();
        CheckUnambiguous(scope, endpoint.Binding, role.ToString());
        await using var binding = await http.ResolveCurrentAsync(scope, endpoint.Binding, role.ToString(), cancellationToken);
        return Http(binding);
    }

    private void CheckUnambiguous(EtlScope scope, string alias, string role)
    {
        if (files.ContainsBinding(scope, alias, role) && http.ContainsBinding(scope, alias, role))
            throw Ambiguous();
    }

    private static EtlConnectorKind FileKind(string role) => role is "Source" or "Lookup" ? EtlConnectorKind.Csv : EtlConnectorKind.Jsonl;
    private static EtlConnectorBindingRevision Http(HostHttpResolvedBinding binding) =>
        new(EtlConnectorKind.Http, new EtlBindingRevision(binding.Alias, binding.Revision, binding.Handle, null));
    private static EtlBindingException Ambiguous() => new("etl.binding-ambiguous", "An ETL alias has ambiguous connector approval for its role.");
    private static EtlBindingException Unavailable() => new("etl.binding-unavailable", "The scoped ETL binding is unavailable for this connector role.");
}
