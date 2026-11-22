using System.Security.Claims;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Authorizes and analyzes a request-only workflow candidate. It has no writer, event or save dependency.</summary>
internal sealed class ActivityDataContextService(IServiceProvider services)
{
    internal async Task<ActivityDataContextResponse> ResolveAsync(HttpContext http, ActivityDataContextRequest request, CancellationToken ct)
    {
        ActivityDataContextResponse Failure(string state, string code, string? baseline = null, string? context = null) =>
            new("1", state, $"activity-data.{code}", "WorkflowDefinition", request.DraftId, baseline,
                request.DocumentRevision, ContextRevision: context);
        if (request.ContractVersion != "1") return Failure("incompatible", "contract-version");
        if (!Valid(request)) return Failure("incompatible", "invalid-request");
        try
        {
            var authorizer = Single<IWorkflowDefinitionAuthorizer>();
            var catalog = Single<IActivityAuthoringCatalog>();
            var policy = Single<IExpressionAuthoringAuthorizationPolicy>();
            var principals = Single<NormalizedPrincipalValidator>();
            if (authorizer is null || catalog is null || policy is null || principals is null)
                return Failure("unavailable", "authority-unavailable");
            if (!principals.TryGetNormalizedPrincipal(http.User, out var principal)) return Failure("denied", "denied");
            var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
            if (tenants is not [var tenant] || string.IsNullOrWhiteSpace(tenant)) return Failure("denied", "denied");
            // Owning definition and current catalog authority precede every draft, target and metadata read.
            if (!await authorizer.CanReadAsync(request.DefinitionId, ct) || !await catalog.CanReadAsync(ct))
                return Failure("denied", "denied");
            var visibleCatalog = await catalog.ListAsync(ct);
            if (!visibleCatalog.Allowed) return Failure("denied", "denied");
            var authorization = await ExpressionToolingApiHandlers.AuthorizationAsync(principal, policy, ct);
            if (!authorization.IsAuthorized) return Failure("denied", "denied");
            // Expression-shaped host filters cannot be applied honestly to both neutral resource kinds.
            // Refuse this composition rather than disclose metadata while bypassing its restrictions.
            if (services.GetServices<IExpressionAuthoringContextFilter>().Any() ||
                services.GetServices<IExpressionAuthoringSymbolFilter>().Any())
                return Failure("unavailable", "metadata-policy-unavailable");
            var drafts = Single<IWorkflowDefinitionDraftStore>();
            var analyzer = Single<IActivityDataSelectionAnalyzer>();
            var structures = Single<IActivityStructureService>();
            var lookup = Single<IActivityDefinitionLookup>();
            if (drafts is null || analyzer is null || structures is null || lookup is null)
                return Failure("unavailable", "analysis-unavailable");
            var draft = await drafts.FindByIdAsync(request.DraftId, ct);
            if (draft is null || draft.Id != request.DraftId || draft.WorkflowDefinitionId != request.DefinitionId || draft.TenantId != tenant)
                return Failure("denied", "denied");
            var baseline = WorkflowAuthoringRevision.For(draft);
            if (request.ExpectedBaseRevision != baseline) return Failure("stale", "base-changed", baseline);
            var snapshot = WorkflowAuthoringRevision.Snapshot(request.Snapshot ?? draft.State.ToStateView());
            if (request.ExpectedSnapshotHash is { } claimed && claimed != snapshot.Hash)
                return Failure("incompatible", "snapshot-hash");
            var document = new ActivityDataSelectionDocument("WorkflowDefinition", draft.Id,
                WorkflowAuthoringRevision.Digest([baseline, snapshot.Hash]), snapshot.State.RootActivity,
                [.. snapshot.State.Inputs], [.. snapshot.State.Variables], [.. snapshot.State.Outputs]);
            if (!CatalogAllows(snapshot.State.RootActivity, structures, visibleCatalog, ct)) return Failure("denied", "denied");
            var scopeRevision = CatalogScopeRevision(visibleCatalog);
            var catalogRevision = await CatalogRevisionAsync(snapshot.State.RootActivity, structures, lookup, ct);
            var analysis = await analyzer.AnalyzeAsync(document, new(request.NodeId, request.InputKey), request.Query, ct);
            if (analysis.ContractRevision != document.ContractRevision) return Failure("unavailable", "invalid-analysis");
            // A refused source or target is disclosed with its findings, after the same rechecks as a supported answer.
            var invalid = analysis.Target is null || !analysis.Findings.IsEmpty;
            var queryRevision = WorkflowAuthoringRevision.JsonDigest(JsonSerializer.SerializeToElement(
                request.Query ?? new(), WorkflowsDesignJsonOptions.WireContext.ActivityDataSelectionQuery));
            var metadataRevision = WorkflowAuthoringRevision.JsonDigest(JsonSerializer.SerializeToElement(
                analysis, WorkflowsDesignJsonOptions.WireContext.ActivityDataSelectionAnalysis));
            var contextRevision = WorkflowAuthoringRevision.Digest([
                baseline, snapshot.Hash, request.DocumentRevision, request.NodeId, request.InputKey, queryRevision,
                scopeRevision, catalogRevision, metadataRevision, authorization.PermissionRevision ?? "", authorization.PolicyFingerprint ?? "",
                ProviderRevision(services)
            ]);
            // A save or access change while metadata was awaited invalidates this result before disclosure.
            if (!principals.TryGetNormalizedPrincipal(http.User, out var currentPrincipal) ||
                !await authorizer.CanReadAsync(request.DefinitionId, ct) || !await catalog.CanReadAsync(ct))
                return Failure("denied", "denied");
            var currentAuthorization = await ExpressionToolingApiHandlers.AuthorizationAsync(currentPrincipal, policy, ct);
            if (!currentAuthorization.IsAuthorized) return Failure("denied", "denied");
            if (currentAuthorization.PermissionRevision != authorization.PermissionRevision || currentAuthorization.PolicyFingerprint != authorization.PolicyFingerprint)
                return Failure("stale", "authority-changed");
            var currentCatalog = await catalog.ListAsync(ct);
            if (!currentCatalog.Allowed || !CatalogAllows(snapshot.State.RootActivity, structures, currentCatalog, ct)) return Failure("denied", "denied");
            if (CatalogScopeRevision(currentCatalog) != scopeRevision ||
                await CatalogRevisionAsync(snapshot.State.RootActivity, structures, lookup, ct) != catalogRevision)
                return Failure("stale", "catalog-changed");
            var current = await drafts.FindByIdAsync(request.DraftId, ct);
            if (current is null || current.Id != draft.Id || current.WorkflowDefinitionId != request.DefinitionId || current.TenantId != tenant)
                return Failure("denied", "denied");
            var currentBaseline = WorkflowAuthoringRevision.For(current);
            if (currentBaseline != baseline) return Failure("stale", "base-changed", currentBaseline);
            if (request.PreviousContextRevision is { } previous && previous != contextRevision)
                return Failure("stale", "context-changed", baseline, contextRevision);
            return new("1", invalid ? "invalid" : analysis.Sources.IsEmpty ? "empty" : "supported",
                invalid ? "activity-data.invalid-source-or-target" : "activity-data.metadata", "WorkflowDefinition",
                draft.Id, baseline, request.DocumentRevision, snapshot.Hash, contextRevision, analysis);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException) { return Failure("incompatible", "invalid-snapshot"); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Authored paths, provider exception text and candidate content are never error messages or log fields.
            return Failure("unavailable", "analysis-unavailable");
        }
    }

    private T? Single<T>() where T : class
    {
        var candidates = services.GetServices<T>().Take(2).ToArray();
        return candidates is [var candidate] ? candidate : null;
    }

    internal static string ProviderRevision(IServiceProvider services) => WorkflowAuthoringRevision.Digest(
        services.GetServices<IActivitySerializedShapeSource>().Select(source => source.GetType())
            .Concat(services.GetServices<IActivityInputSourceConstraintProvider>().Select(source => source.GetType()))
            .Select(type => $"{type.FullName}:{type.Module.ModuleVersionId}").Order(StringComparer.Ordinal));

    internal static string CatalogScopeRevision(ActivityAuthoringCatalogResult catalog) => WorkflowAuthoringRevision.Digest(
        catalog.Entries.Select(entry => WorkflowAuthoringRevision.Digest([
            entry.ActivityVersionId, entry.ActivityTypeKey, entry.Version, entry.Available ? "1" : "0",
            .. entry.Inputs.Select(input => WorkflowAuthoringRevision.Digest([input.ReferenceKey, input.Name, input.Type,
                input.IsRequired ? "1" : "0", input.IsSensitive ? "1" : "0", input.IsCredential ? "1" : "0", input.UiHint ?? ""]))
        ])).Order(StringComparer.Ordinal));

    internal static bool CatalogAllows(ActivityNode? root, IActivityStructureService structures, ActivityAuthoringCatalogResult catalog, CancellationToken ct)
    {
        if (!catalog.Allowed) return false;
        var permitted = catalog.Entries.Select(entry => entry.ActivityVersionId).ToHashSet(StringComparer.Ordinal);
        if (permitted.Count != catalog.Entries.Count || permitted.Any(string.IsNullOrWhiteSpace)) return false;
        var pending = new Stack<(ActivityNode Node, int Depth)>();
        if (root is not null) pending.Push((root, 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var item))
        {
            ct.ThrowIfCancellationRequested();
            if (visited.Count >= 10000 || item.Depth > 100 || !visited.Add(item.Node.NodeId)) return false;
            // Check a node before projecting its private children. Addability is not read visibility.
            if (item.Node.Intrinsic is null && !permitted.Contains(item.Node.ActivityVersionId)) return false;
            if (item.Node.Structure is { } structure && !structures.HasHandler(structure))
                throw new NotSupportedException("The owning structure contribution is unavailable.");
            foreach (var child in structures.ProjectChildren(item.Node).SelectMany(slot => slot.Activities)) pending.Push((child, item.Depth + 1));
        }
        return true;
    }

    private static bool Valid(ActivityDataContextRequest request) =>
        new[] { request.DefinitionId, request.DraftId, request.ExpectedBaseRevision, request.DocumentRevision, request.NodeId, request.InputKey }
            .All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024)
        && (request.ExpectedSnapshotHash?.Length ?? 0) <= 256 && (request.PreviousContextRevision?.Length ?? 0) <= 256
        && (request.Query is null || request.Query is { Skip: >= 0 and <= 10000, Take: >= 1 and <= 100,
            ShapeLimits: { MaximumDepth: >= 0 and <= 16, MaximumMembers: >= 0 and <= 512 } }
            && (request.Query.Search?.Length ?? 0) <= 256 && !request.Query.MemberPath.IsDefault && request.Query.MemberPath.Length <= 64
            && request.Query.MemberPath.All(segment => segment is not null));

    internal static async Task<string> CatalogRevisionAsync(ActivityNode? root, IActivityStructureService structures, IActivityDefinitionLookup lookup, CancellationToken ct)
    {
        var pending = new Stack<(ActivityNode Node, int Depth)>();
        if (root is not null) pending.Push((root, 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var facts = new List<string>();
        while (pending.TryPop(out var item))
        {
            ct.ThrowIfCancellationRequested();
            if (visited.Count >= 10000 || item.Depth > 100 || !visited.Add(item.Node.NodeId))
                throw new JsonException("Invalid canonical graph.");
            if (item.Node.Intrinsic is null && versions.Add(item.Node.ActivityVersionId))
            {
                var version = await lookup.FindVersion(item.Node.ActivityVersionId, ct);
                facts.Add(version is null ? $"missing:{item.Node.ActivityVersionId}" : WorkflowAuthoringRevision.Digest([
                    version.Id, version.Hash ?? "", version.ProviderKey, version.ProviderSchemaVersion,
                    version.ConsumerKey, version.ConsumerSchemaVersion
                ]));
            }
            foreach (var child in structures.ProjectChildren(item.Node).SelectMany(slot => slot.Activities)) pending.Push((child, item.Depth + 1));
        }
        return WorkflowAuthoringRevision.Digest(facts.Order(StringComparer.Ordinal));
    }
}
