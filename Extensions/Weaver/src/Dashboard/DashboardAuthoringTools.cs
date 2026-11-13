using System.Text;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Core.Exceptions;

namespace Elsa.Weaver.Dashboard;

// The engine owns invocation identity/policy. This module owns the meaning of the draft and
// re-reads its current authority at preparation and execution. No tool writes a saved resource.
internal abstract class DashboardAuthoringTool(DashboardWeaverAccess access, IDashboardAuthoringService authoring,
    IDashboardResourceService resources) : IWeaverTool, IWeaverToolAuthorization
{
    public abstract WeaverToolDescriptor Descriptor { get; }
    protected DashboardWeaverAccess Access => access;

    public ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target,
        CancellationToken cancellationToken = default) => access.AuthorizeTargetAsync(actorId, tenantId, target, resources, cancellationToken);

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        try
        {
            await access.RequireActorAsync(invocation.ActorId, invocation.TenantId, cancellationToken);
            await access.RequireSessionAsync(invocation.SessionId, cancellationToken);
            var contexts = invocation.Context.Where(x => x.ContentType == DashboardWeaverProtocol.ContextKind &&
                x.ContributorId == DashboardWeaverProtocol.SourceId && x.Origin == WeaverContextOrigin.AuthoritativeRead).ToArray();
            if (contexts.Length != 1) throw new DashboardResourceException("validation", "One admitted Dashboard question draft is required.");
            var draft = DashboardWeaverProtocol.ReadDraft(contexts[0]);
            if (invocation.Client != DashboardWeaverProtocol.Client(draft))
                throw new DashboardResourceException("conflict", "The authoring request belongs to another editor.");
            var current = await authoring.PrepareAsync(draft, cancellationToken);
            var input = DashboardWeaverProtocol.Element(invocation.Arguments);
            var payload = await ProduceAsync(current, input, cancellationToken);
            Bound(payload.Data);
            var frozen = DashboardWeaverProtocol.Element(new PreparedDashboardTool(draft, input));
            Bound(frozen);
            return WeaverResult<WeaverToolPreparation>.Success(new(DashboardWeaverProtocol.Target(draft.Target), frozen,
                WeaverRisk.ReadOnly, payload, false) { Client = invocation.Client });
        }
        catch (DashboardResourceException error) { return Failure<WeaverToolPreparation>(error); }
        catch (DashboardQueryException) { return SourceUnavailable<WeaverToolPreparation>(); }
        catch (JsonException error) { return Invalid<WeaverToolPreparation>(error); }
    }

    public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        try
        {
            await access.RequireActorAsync(invocation.ActorId, invocation.TenantId, cancellationToken);
            await access.RequireSessionAsync(invocation.SessionId, cancellationToken);
            if (invocation.ModuleId != DashboardWeaverProtocol.ModuleId || invocation.ToolName != Descriptor.Name ||
                invocation.ToolContractVersion != Descriptor.ContractVersion || invocation.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new DashboardResourceException("conflict", "Prepare a current Dashboard authoring request.");
            var frozen = invocation.Arguments.Deserialize<PreparedDashboardTool>(DashboardWeaverProtocol.Json)
                ?? throw new DashboardResourceException("validation", "The authoring preparation is missing.");
            if (invocation.Target is null || !DashboardWeaverProtocol.SameTarget(invocation.Target, DashboardWeaverProtocol.Target(frozen.Draft.Target)) ||
                invocation.Client != DashboardWeaverProtocol.Client(frozen.Draft))
                throw new DashboardResourceException("conflict", "The proposal belongs to another question or editor.");
            var current = await authoring.PrepareAsync(frozen.Draft, cancellationToken);
            var payload = await ProduceAsync(current, frozen.Input, cancellationToken);
            Bound(payload.Data);
            return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
                "Read current authorized declarations. No question was applied, saved or shared; no result data was sent to the model.", payload));
        }
        catch (DashboardResourceException error) { return Failure<WeaverToolResult>(error); }
        catch (DashboardQueryException) { return SourceUnavailable<WeaverToolResult>(); }
        catch (JsonException error) { return Invalid<WeaverToolResult>(error); }
    }

    protected abstract ValueTask<WeaverPayload> ProduceAsync(DashboardAuthoringContext context, JsonElement input, CancellationToken cancellationToken);

    protected static T Read<T>(JsonElement input) => input.Deserialize<T>(DashboardWeaverProtocol.Json)
        ?? throw new DashboardResourceException("validation", "Supply the supported tool arguments.");

    protected static WeaverToolDescriptor Contract(string name, string label, string description, string schema, string resultType) =>
        new(name, label, description, WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, schema,
            ["studio/questions:read"], ["/questions", "/dashboards"])
        { ModuleId = DashboardWeaverProtocol.ModuleId, ContractVersion = "1", DisclosureSourceId = DashboardWeaverProtocol.SourceId,
            ResultType = resultType, ResultSchemaVersion = "1", ResultRendererId = resultType };

    private static void Bound(JsonElement data)
    {
        if (Encoding.UTF8.GetByteCount(data.GetRawText()) > DashboardWeaverProtocol.Budget)
            throw new DashboardResourceException("tooLarge", "The authoring data is too large. Select one dataset or simplify the question.");
    }

    private static WeaverResult<T> Failure<T>(DashboardResourceException error) => WeaverResult<T>.Failure("dashboards.authoring." + error.Code,
        error.Code is "unavailable" or "schemaIncompatible" ? "Current Dashboard authoring support is unavailable." : error.Message,
        error.Code switch { "unavailable" or "schemaIncompatible" => 503, "notFound" => 404, "conflict" => 409, "denied" => 403, "tooLarge" => 413, _ => 422 });
    private static WeaverResult<T> Invalid<T>(JsonException error) => WeaverResult<T>.Failure("dashboards.authoring.invalid",
        "Invalid declarative arguments at " + GrammarPath(error.Path) + ". Follow the tool schema: all non-null scalar literals are JSON strings; " +
        "IN uses values:[{type,literal}]; weekStart is \"monday\"; HAVING uses metric, not field. Omit unsupported members.", 422);

    // Never echo exception text, arbitrary property names, parameter names or submitted values.
    private static string GrammarPath(string? path)
    {
        HashSet<string> members = ["question", "query", "schemaVersion", "dataset", "id", "schemaRevision", "form", "parameters",
            "name", "type", "required", "nullable", "default", "filters", "field", "metric", "operator", "value", "values", "literal",
            "parameter", "all", "any", "fields", "groupBy", "metrics", "alias", "revision", "aggregate", "time", "window", "from", "to",
            "timeZone", "weekStart", "bucket", "comparison", "mode", "having", "sort", "reference", "direction", "nulls", "limit",
            "visualization", "kind", "x", "y", "label", "parameterValues"];
        var safe = new StringBuilder("$");
        foreach (var part in (path ?? "").Split('.').Skip(1).Take(12))
        {
            var member = part.Split('[')[0];
            if (!members.Contains(member)) { safe.Append(".<member>"); break; }
            safe.Append('.').Append(member);
            if (part.Contains('[')) safe.Append("[]");
            // parameterValues keys are caller data even when they happen to match a grammar word.
            if (member == "parameterValues") break;
        }
        return safe.ToString();
    }
    private static WeaverResult<T> SourceUnavailable<T>() => WeaverResult<T>.Failure("dashboards.authoring.source_unavailable",
        "Current dataset support could not be established. Retry after source support recovers.", 503);
    private sealed record PreparedDashboardTool(DashboardAuthoringDraft Draft, JsonElement Input);
}

internal sealed class DashboardDatasetTool(DashboardWeaverAccess access, IDashboardAuthoringService authoring,
    IDashboardResourceService resources, IDashboardQueryService queries) : DashboardAuthoringTool(access, authoring, resources)
{
    public override WeaverToolDescriptor Descriptor { get; } = Contract("dashboard_discover_datasets", "Discover Dashboard datasets",
        "Read currently authorized module-owned dataset declarations, fields, metric revisions, supported operations and time/coverage meanings. " +
        "Optional datasetId restricts the catalog. No source records or aggregate results are disclosed.",
        """{"type":"object","properties":{"datasetId":{"type":"string","minLength":1,"maxLength":256}},"additionalProperties":false}""",
        DashboardWeaverProtocol.CatalogType);
    protected override async ValueTask<WeaverPayload> ProduceAsync(DashboardAuthoringContext context, JsonElement input, CancellationToken cancellationToken)
    {
        var args = Read<CatalogInput>(input);
        if (args.DatasetId is not null && !DashboardWeaverProtocol.Identity(args.DatasetId, 256))
            throw new DashboardResourceException("validation", "Select an installed dataset identity.");
        var actor = await Access.CurrentAsync(cancellationToken);
        var current = await queries.DiscoverAsync(Access.Scope(actor), cancellationToken);
        IReadOnlyList<DashboardDatasetDeclaration> selected = args.DatasetId is null ? current :
            current.Where(x => x.Id == args.DatasetId).ToArray();
        return new WeaverPayload(DashboardWeaverProtocol.CatalogType, "1", DashboardWeaverProtocol.Element(new DashboardCatalogResult(selected)));
    }
    private sealed record CatalogInput(string? DatasetId = null);
}

internal sealed class DashboardQuestionTool(DashboardWeaverAccess access, IDashboardAuthoringService authoring,
    IDashboardResourceService resources, IDashboardQueryService queries) : DashboardAuthoringTool(access, authoring, resources)
{
    public override WeaverToolDescriptor Descriptor { get; } = Contract("dashboard_preview_question", "Propose a Dashboard question",
        "Validate a newly composed declarative question against currently authorized dataset metadata. Studio executes the actual preview " +
        "natively; this tool never returns source records or numeric results. It never applies, saves or shares. Query fields/metric revisions, " +
        "scalar types/operators/time windows come from dashboard_discover_datasets. Use string literals for all scalar types, typed parameters, " +
        "current./baseline./delta. references for having and sorting, and supported visualization bindings. Limit aggregates to 200 rows and records to 100 rows. Ask consequential clarifications first.",
        DashboardQuestionSchema.Json,
        DashboardWeaverProtocol.ProposalType);
    protected override async ValueTask<WeaverPayload> ProduceAsync(DashboardAuthoringContext context, JsonElement input, CancellationToken cancellationToken)
    {
        if (!context.CanEdit) throw new DashboardResourceException("denied", "Copy this question into your own draft before refining it.");
        var question = Read<QuestionInput>(input).Question
            ?? throw new DashboardResourceException("validation", "Supply one declarative question.");
        var actor = await Access.CurrentAsync(cancellationToken);
        var validation = await queries.ValidateAsync(Access.Scope(actor), question, cancellationToken);
        if (!validation.IsValid)
            throw new DashboardResourceException("validation", "Question validation failed: " + string.Join("; ", validation.Problems.Take(8).Select(x => x.Message)));
        var dataset = (await queries.DiscoverAsync(Access.Scope(actor), cancellationToken)).SingleOrDefault(x => x.Id == question.Query.Dataset.Id);
        if (dataset is null || dataset.SchemaRevision != question.Query.Dataset.SchemaRevision)
            throw new DashboardResourceException("conflict", "Dataset support changed. Discover and preview the current source again.");
        var interpretation = $"{dataset.Title}. {dataset.Description} This is an unsaved declarative question; inspect its native preview and coverage before using it.";
        var proposal = new DashboardAuthoringProposal(context.Draft.Target, context.Draft.Client, context.Draft.WorkingRevision,
            question, DashboardWeaverProtocol.Fingerprint(question), null, interpretation);
        return new(DashboardWeaverProtocol.ProposalType, "1", DashboardWeaverProtocol.Element(proposal));
    }
    private sealed record QuestionInput(DashboardQueryRequest? Question);
}

internal sealed class DashboardClarificationTool(DashboardWeaverAccess access, IDashboardAuthoringService authoring,
    IDashboardResourceService resources) : DashboardAuthoringTool(access, authoring, resources)
{
    public override WeaverToolDescriptor Descriptor { get; } = Contract("dashboard_request_clarification", "Clarify question meaning",
        "Ask one consequential question about the intended dataset, measure, grouping or time meaning before proposing. " +
        "Offer two to four mutually exclusive interpretations. Tool success does not confirm an answer; only a subsequent user response does.",
        """{"type":"object","required":["question","choices"],"properties":{"question":{"type":"string","minLength":1,"maxLength":512},"choices":{"type":"array","minItems":2,"maxItems":4,"items":{"type":"object","required":["id","label","description"],"properties":{"id":{"type":"string","minLength":1,"maxLength":64},"label":{"type":"string","minLength":1,"maxLength":128},"description":{"type":"string","minLength":1,"maxLength":512}},"additionalProperties":false}}},"additionalProperties":false}""",
        DashboardWeaverProtocol.ClarificationType);
    protected override ValueTask<WeaverPayload> ProduceAsync(DashboardAuthoringContext context, JsonElement input, CancellationToken cancellationToken)
    {
        var question = Read<DashboardAuthoringClarification>(input);
        if (!DashboardWeaverProtocol.Identity(question.Question, 512) || question.Choices is null || question.Choices.Count is < 2 or > 4 ||
            question.Choices.Any(choice => choice is null) ||
            question.Choices.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != question.Choices.Count ||
            question.Choices.Any(x => !DashboardWeaverProtocol.Identity(x.Id, 64) || !DashboardWeaverProtocol.Identity(x.Label, 128) ||
                !DashboardWeaverProtocol.Identity(x.Description, 512)))
            throw new DashboardResourceException("validation", "Ask one bounded question with two to four distinct choices.");
        return ValueTask.FromResult(new WeaverPayload(DashboardWeaverProtocol.ClarificationType, "1",
            DashboardWeaverProtocol.Element(new DashboardClarificationResult(context.Draft.Target, context.Draft.Client,
                context.Draft.WorkingRevision, question))));
    }
}

internal sealed record DashboardClarificationResult(DashboardAuthoringTarget Target, DashboardAuthoringClient Client,
    string WorkingRevision, DashboardAuthoringClarification Clarification);
