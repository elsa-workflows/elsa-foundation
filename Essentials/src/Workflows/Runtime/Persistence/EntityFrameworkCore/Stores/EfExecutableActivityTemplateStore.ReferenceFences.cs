using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfExecutableActivityTemplateStore
{
    // Staged updates share the referencer's eventual SaveChanges/transaction. All material discovery is done
    // before SaveChanges acquires write locks. No scope-wide scan runs while candidate rows are locked.
    internal static Task StageWorkflowTemplateFencesAsync(RuntimeDbContext owner, string scope,
        IEnumerable<WorkflowExecutable> executables, CancellationToken cancellationToken) =>
        StageTemplateFencesAsync(owner, scope, executables.SelectMany(executable =>
            MetadataTargets(executable.NodesById.Values.Select(node => node.Metadata).Prepend(executable.CompatibilityMetadata))), cancellationToken);

    internal static Task StageSourceTemplateFenceAsync(RuntimeDbContext owner, string scope, string artifactId,
        CancellationToken cancellationToken) =>
        StageTemplateFencesAsync(owner, scope, [new ReferenceTarget(artifactId, null, false)], cancellationToken);

    internal Task StagePublicationFencesAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken) =>
        StageTemplateFencesAsync(context, RequireScope(),
            DependencyTargets(template, requireExact: true).Append(new ReferenceTarget(template.TemplateId, template.TemplateHash, true)), cancellationToken);

    private static Task StageTemplateDependencyFencesAsync(RuntimeDbContext owner, string scope,
        ExecutableActivityTemplate template, bool requireExact, CancellationToken cancellationToken) =>
        StageTemplateFencesAsync(owner, scope, DependencyTargets(template, requireExact), cancellationToken);

    private static IEnumerable<ReferenceTarget> DependencyTargets(ExecutableActivityTemplate template, bool requireExact) =>
        template.ClosedTemplates.Select(identity => new ReferenceTarget(identity.TemplateId, identity.TemplateHash, requireExact))
            .Concat(template.DirectDependencies.Select(dependency => new ReferenceTarget(dependency.TemplateId, dependency.TemplateHash, requireExact)))
            .Concat(MetadataTargets(template.NodesById.Values.Select(node => node.Metadata).Prepend(template.CompatibilityMetadata)));

    private static IEnumerable<ReferenceTarget> MetadataTargets(IEnumerable<IReadOnlyDictionary<string, string>> metadata) =>
        metadata.Where(values => values.TryGetValue("activity.templateHash", out var hash) && !string.IsNullOrWhiteSpace(hash))
            .Select(values => new ReferenceTarget(null, values["activity.templateHash"], false));

    private static async Task StageTemplateFencesAsync(RuntimeDbContext owner, string scope,
        IEnumerable<ReferenceTarget> requested, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new Dictionary<string, ExecutableActivityTemplateEntity>(StringComparer.Ordinal);
        foreach (var target in requested.Distinct().OrderBy(target => target.Id, StringComparer.Ordinal).ThenBy(target => target.Hash, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var added = owner.ChangeTracker.Entries<ExecutableActivityTemplateEntity>()
                .Where(entry => entry.State == EntityState.Added && entry.Entity.ScopeKey == Encode(scope))
                .Select(entry => entry.Entity)
                .SingleOrDefault(row => target.Id is not null ? row.Id == CreateId(scope, target.Id) : row.TemplateHash == target.Hash);
            if (added is not null)
            {
                var staged = Read(added, new TemplateIdentity(scope, Decode(added.TemplateId), added.TemplateHash));
                if (target.Required && target.Hash is not null && target.Hash != staged.TemplateHash)
                    throw new InvalidDataException("A staged template dependency has a different hash.");
                continue; // New material and its referencer become visible in this same commit.
            }

            ExecutableActivityTemplateEntity? row;
            ExecutableActivityTemplateHashClaimEntity? claim = null;
            if (target.Id is not null)
            {
                // The physical key establishes ownership; projections must be validated after lookup,
                // otherwise a corrupt projection can masquerade as an optional missing import.
                row = await owner.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.Id == CreateId(scope, target.Id), cancellationToken);
            }
            else
            {
                claim = await owner.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.Id == HashClaimId(scope, target.Hash!), cancellationToken);
                if (claim is null)
                {
                    var orphan = await owner.ExecutableActivityTemplates.AsNoTracking().AnyAsync(candidate =>
                        (candidate.ScopeKeyHash == Hash(scope) || candidate.ScopeKey == Encode(scope)) &&
                        (candidate.TemplateHash == target.Hash || candidate.TemplateHashHash == Hash(target.Hash!)), cancellationToken);
                    if (orphan) throw new InvalidDataException("Template material exists without its physical hash claim.");
                    if (target.Required) throw new InvalidDataException("An exact template hash claim is missing.");
                    continue;
                }
                var claimed = ReadClaim(claim, new TemplateIdentity(scope, null, target.Hash));
                row = await owner.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.Id == CreateId(scope, claimed.TemplateId), cancellationToken)
                    ?? throw new InvalidDataException("A template hash claim points to missing material.");
            }
            if (row is null)
            {
                if (target.Required) throw new InvalidDataException($"Exact template dependency '{target.Id}' is missing.");
                continue; // Preserve inert parent-first staging and legitimate inlined imports without a template store.
            }
            string actualId;
            try { actualId = target.Id ?? Decode(row.TemplateId); }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            { throw new InvalidDataException("The template identity projection is corrupt.", exception); }
            var identity = new TemplateIdentity(scope, actualId, row.TemplateHash);
            var material = Read(row, identity);
            if (target.Required && target.Hash is not null && target.Hash != material.TemplateHash)
                throw new InvalidDataException($"Exact template dependency '{target.Id}' has a different hash.");
            claim ??= await owner.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.Id == HashClaimId(scope, material.TemplateHash), cancellationToken)
                ?? throw new InvalidDataException("Template material has no physical hash claim.");
            EnsureOwnedClaim(claim, identity); EnsureMatchingIncarnation(row, claim);
            rows[row.Id] = row;
        }

        foreach (var row in rows.Values.OrderBy(row => row.Id, StringComparer.Ordinal))
        {
            var tracked = owner.ChangeTracker.Entries<ExecutableActivityTemplateEntity>().SingleOrDefault(entry => entry.Entity.Id == row.Id);
            if (tracked is null) tracked = owner.Attach(row);
            else if (tracked.Entity.IncarnationId != row.IncarnationId || tracked.OriginalValues.GetValue<long>(nameof(row.Revision)) != row.Revision)
                throw new DbUpdateConcurrencyException("Template material changed while its referencer was staged.");
            if (tracked.State != EntityState.Modified)
            {
                tracked.Entity.Revision = checked(row.Revision + 1);
                tracked.Property(entity => entity.Revision).IsModified = true;
            }
        }
    }

    private sealed record ReferenceTarget(string? Id, string? Hash, bool Required);
}
