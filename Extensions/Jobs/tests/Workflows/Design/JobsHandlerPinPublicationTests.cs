using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Design;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Workflows.Design.Tests;

/// <summary>Source composition tests of the registered Publishing fan-in, without a Jobs handler or authority.</summary>
public sealed class JobsHandlerPinPublicationTests
{
    private const string ExpectedDescriptor = """
        {"FormatVersion":1,"Handler":{"JobType":"reports.monthly","Version":"2.1.0"},"InputSchema":{"Alias":"reports.input","Version":1},"ResultSchema":{"Alias":"reports.result","Version":2},"Queue":"reports","Policy":{"Retry":{"MaximumAttempts":3,"InitialBackoff":"00:00:01","MaximumBackoff":"00:01:00","BackoffMultiplier":2,"JitterFraction":0,"RetryTimeouts":true},"AttemptTimeout":"00:05:00","MinimumKeyRecognition":"30.00:00:00"},"AssociationMode":0}
        """;
    private const string ExpectedDescriptorSha256 = "d631685037c2cc573372f776305ef23c555db5fa7d01afdfbc5d6c55f7e79b05";
    private static readonly JsonSerializerOptions LiteralJson = new(JsonSerializerDefaults.Web);
    private static readonly ValueTypeDescriptor LiteralType = new("Object");

    [Fact]
    public async Task Registered_publication_fan_in_freezes_exact_pin_without_a_coordinator_handler_installation()
    {
        var original = JobNode();
        var services = PublicationServices();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<IJobCommands>());
        Assert.Null(scope.ServiceProvider.GetService<IJobQueries>());
        var enricher = scope.ServiceProvider.GetRequiredService<IExecutableNodeMetadataEnricher>();
        Assert.IsType<ExecutableNodeMetadataEnricher>(enricher);

        var enriched = await enricher.EnrichCompilationAsync(Request(), Source(), original);

        var descriptor = enriched.RootActivity.Metadata[RunJob.PinMetadataKey];
        Assert.Equal(ExpectedDescriptor, descriptor);
        Assert.Equal(ExpectedDescriptorSha256, Hash(descriptor));
        Assert.Equal(Pin(), JobWorkflowHandlerPin.Parse(descriptor));
        Assert.Empty(enriched.Dependencies);
        Assert.False(original.Metadata.ContainsKey(RunJob.PinMetadataKey));
        Assert.Equal("preserved", enriched.RootActivity.Metadata["other-domain"]);
    }

    [Fact]
    public async Task Feature_registration_is_idempotent_scoped_and_resolves_without_worker_services()
    {
        var services = new ServiceCollection();
        var feature = new JobsWorkflowsDesignFeature();
        feature.ConfigureServices(services);
        feature.ConfigureServices(services);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IExecutableCompilationSource));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var contributor = first.ServiceProvider.GetRequiredService<IExecutableCompilationSource>();
        Assert.Same(contributor, first.ServiceProvider.GetRequiredService<IExecutableCompilationSource>());
        Assert.NotSame(contributor, second.ServiceProvider.GetRequiredService<IExecutableCompilationSource>());
        var contribution = await contributor.GetContributionAsync(new(Request(), Source(), JobNode()));
        var claim = Assert.Single(contribution.NodeMetadata);
        Assert.Equal(RunJob.PinMetadataKey, claim.Key);
        Assert.Equal(ExpectedDescriptor, claim.Value);
    }

    [Fact]
    public async Task Materialized_input_and_not_before_bindings_survive_publication_without_becoming_fixed_pin_fields()
    {
        var bindings = Bindings();
        var input = RequestBinding(nameof(RunJob.Input), "invoice-lines");
        var notBefore = new RuntimeInputBinding(nameof(RunJob.NotBefore), LiteralType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference("due-time", "root"));
        bindings[nameof(RunJob.Input)] = input;
        bindings[nameof(RunJob.NotBefore)] = notBefore;
        var enriched = await EnrichAsync(JobNode(bindings));

        Assert.Same(input, enriched.RootActivity.InputBindings[nameof(RunJob.Input)]);
        Assert.Same(notBefore, enriched.RootActivity.InputBindings[nameof(RunJob.NotBefore)]);
        Assert.Equal(ExpectedDescriptor, enriched.RootActivity.Metadata[RunJob.PinMetadataKey]);

        bindings[nameof(RunJob.Input)] = Literal(nameof(RunJob.Input), new { account = "another-account" });
        bindings[nameof(RunJob.NotBefore)] = Literal(nameof(RunJob.NotBefore), DateTimeOffset.UnixEpoch);
        var changed = await EnrichAsync(JobNode(bindings));
        Assert.Equal(ExpectedDescriptor, changed.RootActivity.Metadata[RunJob.PinMetadataKey]);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler))]
    [InlineData(nameof(RunJob.InputSchema))]
    [InlineData(nameof(RunJob.ResultSchema))]
    [InlineData(nameof(RunJob.Queue))]
    [InlineData(nameof(RunJob.Policy))]
    [InlineData(nameof(RunJob.AssociationMode))]
    public async Task Fixed_selections_refuse_workflow_request_bindings(string inputKey)
    {
        var bindings = Bindings();
        bindings[inputKey] = RequestBinding(inputKey, "private-selection-canary");
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains(inputKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("requires a literal", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-selection-canary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fixed_handler_expression_is_refused_without_evaluation_or_expression_text_in_the_error()
    {
        var bindings = Bindings();
        bindings[nameof(RunJob.Handler)] = new(nameof(RunJob.Handler), LiteralType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression, expression: new RuntimeExpressionBinding("javascript", "private-expression-canary"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains("requires a literal 'Handler'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-expression-canary", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler))]
    [InlineData(nameof(RunJob.InputSchema))]
    [InlineData(nameof(RunJob.ResultSchema))]
    [InlineData(nameof(RunJob.Queue))]
    [InlineData(nameof(RunJob.Policy))]
    [InlineData(nameof(RunJob.AssociationMode))]
    public async Task Fixed_selections_refuse_secret_references_with_the_shared_safe_diagnostic(string inputKey)
    {
        var bindings = Bindings();
        bindings[inputKey] = new(inputKey, LiteralType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference("private-secret-reference-canary"));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains(SecretBindingDiagnostics.SecretBindingRefusedCode, exception.Message, StringComparison.Ordinal);
        Assert.Contains(inputKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-secret-reference-canary", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler))]
    [InlineData(nameof(RunJob.InputSchema))]
    [InlineData(nameof(RunJob.ResultSchema))]
    [InlineData(nameof(RunJob.Queue))]
    [InlineData(nameof(RunJob.Policy))]
    [InlineData(nameof(RunJob.AssociationMode))]
    public async Task Withheld_fixed_selections_are_refused_before_literal_deserialization(string inputKey)
    {
        var bindings = Bindings();
        bindings[inputKey] = new(inputKey, LiteralType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Withheld(LiteralType,
                WithheldValue.SecretReference(new RuntimeSecretReference("private-withheld-reference-canary"), null),
                ValueProtectionPolicy.InstanceInline));
        var exception = await Assert.ThrowsAsync<WithheldValueException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains(SecretBindingDiagnostics.WithheldInputCode, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-withheld-reference-canary", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler))]
    [InlineData(nameof(RunJob.InputSchema))]
    [InlineData(nameof(RunJob.ResultSchema))]
    [InlineData(nameof(RunJob.Queue))]
    [InlineData(nameof(RunJob.Policy))]
    [InlineData(nameof(RunJob.AssociationMode))]
    public async Task Missing_fixed_selections_are_not_replaced_by_design_defaults(string inputKey)
    {
        var bindings = Bindings();
        bindings.Remove(inputKey);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains(inputKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("requires a literal", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler), "{\"jobType\":\"\",\"version\":\"2.1.0\"}")]
    [InlineData(nameof(RunJob.InputSchema), "{\"alias\":\"reports.input\",\"version\":0}")]
    [InlineData(nameof(RunJob.ResultSchema), "{\"version\":2}")]
    [InlineData(nameof(RunJob.Queue), "null")]
    [InlineData(nameof(RunJob.Policy), "{\"retry\":null,\"attemptTimeout\":\"00:05:00\",\"minimumKeyRecognition\":\"30.00:00:00\"}")]
    [InlineData(nameof(RunJob.Policy), "{\"retry\":{},\"attemptTimeout\":\"9999999999.00:00:00\",\"minimumKeyRecognition\":\"30.00:00:00\"}")]
    [InlineData(nameof(RunJob.AssociationMode), "1")]
    [InlineData(nameof(RunJob.AssociationMode), "2")]
    [InlineData(nameof(RunJob.AssociationMode), "2147483647")]
    public async Task Malformed_or_unsupported_fixed_literals_are_refused_with_no_payload_in_the_error(string inputKey, string json)
    {
        var bindings = Bindings();
        using var document = JsonDocument.Parse(json);
        bindings[inputKey] = Literal(inputKey, document.RootElement);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.StartsWith("RunJob node 'job-node'", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_fixed_literal_is_rejected_before_deserialization_and_not_echoed()
    {
        var bindings = Bindings();
        var value = "oversized-selection-canary" + new string('x', 32_768);
        bindings[nameof(RunJob.Queue)] = Literal(nameof(RunJob.Queue), value);
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(JobNode(bindings)));
        Assert.Contains("oversized 'Queue'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oversized-selection-canary", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(RunJob.Handler))]
    [InlineData(nameof(RunJob.InputSchema))]
    [InlineData(nameof(RunJob.ResultSchema))]
    [InlineData(nameof(RunJob.Queue))]
    [InlineData(nameof(RunJob.Policy))]
    public async Task A_changed_semantic_selection_changes_the_published_descriptor(string inputKey)
    {
        var bindings = Bindings();
        bindings[inputKey] = inputKey switch
        {
            nameof(RunJob.Handler) => Literal(inputKey, Pin().Handler with { Version = "2.2.0" }),
            nameof(RunJob.InputSchema) => Literal(inputKey, Pin().InputSchema with { Version = 2 }),
            nameof(RunJob.ResultSchema) => Literal(inputKey, Pin().ResultSchema with { Version = 3 }),
            nameof(RunJob.Queue) => Literal(inputKey, "priority-reports"),
            nameof(RunJob.Policy) => Literal(inputKey, Pin().Policy with { AttemptTimeout = TimeSpan.FromMinutes(10) }),
            _ => throw new ArgumentOutOfRangeException(nameof(inputKey))
        };
        var enriched = await EnrichAsync(JobNode(bindings));
        var descriptor = enriched.RootActivity.Metadata[RunJob.PinMetadataKey];
        Assert.NotEqual(ExpectedDescriptorSha256, Hash(descriptor));
        Assert.NotEqual(Pin(), JobWorkflowHandlerPin.Parse(descriptor));
    }

    [Fact]
    public async Task Unequal_existing_pin_is_a_publication_conflict_and_never_silently_repinned()
    {
        var stale = (Pin() with { Queue = "retired-queue" }).Serialize();
        var node = JobNode(metadata: new Dictionary<string, string> { [RunJob.PinMetadataKey] = stale });
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(node));
        Assert.Contains(RunJob.PinMetadataKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("unequal values", exception.Message, StringComparison.Ordinal);
        Assert.Equal(stale, node.Metadata[RunJob.PinMetadataKey]);
    }

    [Fact]
    public async Task Identical_existing_pin_is_idempotent()
    {
        var enriched = await EnrichAsync(JobNode(metadata:
            new Dictionary<string, string> { [RunJob.PinMetadataKey] = ExpectedDescriptor }));
        Assert.Equal(ExpectedDescriptor, enriched.RootActivity.Metadata[RunJob.PinMetadataKey]);
    }

    [Fact]
    public async Task Nested_job_nodes_are_pinned_and_other_activity_nodes_remain_unclaimed()
    {
        var first = JobNode(nodeId: "job-z");
        var second = JobNode(nodeId: "job-a");
        var root = new ExecutableNode("root", "authored-root", "test.Sequence", "1.0.0", Descriptor("test.Sequence"),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("Activities", [first, second])]);
        var enriched = await EnrichAsync(root);
        Assert.Empty(enriched.RootActivity.Metadata);
        Assert.All(Assert.Single(enriched.RootActivity.ChildSlots).Activities,
            node => Assert.Equal(ExpectedDescriptor, node.Metadata[RunJob.PinMetadataKey]));

        var services = new ServiceCollection();
        new JobsWorkflowsDesignFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var contributor = scope.ServiceProvider.GetRequiredService<IExecutableCompilationSource>();
        var contribution = await contributor.GetContributionAsync(new(Request(), Source(), root));
        Assert.Equal(new[] { "job-a", "job-z" }, contribution.NodeMetadata.Select(item => item.ExecutableNodeId));
        var unrelated = new ExecutableNode("other", "other", "test.Other", "1.0.0", Descriptor("test.Other"),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        Assert.Empty((await contributor.GetContributionAsync(new(Request(), Source(), unrelated))).NodeMetadata);
    }

    [Fact]
    public async Task Cancelled_compilation_does_not_return_partial_pin_claims()
    {
        var services = new ServiceCollection();
        new JobsWorkflowsDesignFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var contributor = scope.ServiceProvider.GetRequiredService<IExecutableCompilationSource>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await contributor.GetContributionAsync(new(Request(), Source(), JobNode()), cancellation.Token));
    }

    private static async Task<ExecutableCompilationEnrichment> EnrichAsync(ExecutableNode node)
    {
        await using var provider = PublicationServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IExecutableNodeMetadataEnricher>()
            .EnrichCompilationAsync(Request(), Source(), node);
    }

    [Theory]
    [InlineData(JobAssociationMode.Independent)]
    [InlineData(JobAssociationMode.Detached)]
    public async Task Submission_publication_pins_only_explicit_nonawaiting_modes_without_a_handler(JobAssociationMode mode)
    {
        var bindings = Bindings(); bindings[nameof(SubmitJob.AssociationMode)] = Literal(nameof(SubmitJob.AssociationMode), mode);
        var node = new ExecutableNode("submit-node", "authored-submit", SubmitJob.ActivityType, "1.0.0", Descriptor(SubmitJob.ActivityType),
            bindings, new Dictionary<string, string> { ["other-domain"] = "preserved" });
        var enriched = await EnrichAsync(node);
        var pin = JobWorkflowHandlerPin.Parse(enriched.RootActivity.Metadata[SubmitJob.PinMetadataKey]);
        Assert.Equal(mode, pin.AssociationMode); Assert.Equal(Pin().Handler, pin.Handler); Assert.Empty(enriched.Dependencies);
        Assert.Equal("preserved", enriched.RootActivity.Metadata["other-domain"]);
    }

    [Fact]
    public async Task Submission_publication_refuses_awaited_mode_instead_of_creating_a_hidden_wait()
    {
        var node = new ExecutableNode("submit-node", "authored-submit", SubmitJob.ActivityType, "1.0.0", Descriptor(SubmitJob.ActivityType),
            Bindings(), new Dictionary<string, string>());
        await Assert.ThrowsAsync<ArgumentException>(() => EnrichAsync(node));
    }

    private static ServiceCollection PublicationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new JobsWorkflowsDesignFeature().ConfigureServices(services);
        return services;
    }

    private static JobWorkflowHandlerPin Pin() => new(1, new("reports.monthly", "2.1.0"),
        new("reports.input", 1), new("reports.result", 2), "reports",
        new(new(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 2, 0, true),
            TimeSpan.FromMinutes(5), TimeSpan.FromDays(30)), JobAssociationMode.Awaited);

    private static Dictionary<string, RuntimeInputBinding> Bindings()
    {
        var pin = Pin();
        return new(StringComparer.Ordinal)
        {
            [nameof(RunJob.Handler)] = Literal(nameof(RunJob.Handler), pin.Handler),
            [nameof(RunJob.InputSchema)] = Literal(nameof(RunJob.InputSchema), pin.InputSchema),
            [nameof(RunJob.ResultSchema)] = Literal(nameof(RunJob.ResultSchema), pin.ResultSchema),
            [nameof(RunJob.Queue)] = Literal(nameof(RunJob.Queue), pin.Queue),
            [nameof(RunJob.Policy)] = Literal(nameof(RunJob.Policy), pin.Policy),
            [nameof(RunJob.AssociationMode)] = Literal(nameof(RunJob.AssociationMode), pin.AssociationMode)
        };
    }

    private static RuntimeInputBinding Literal<T>(string inputKey, T value) => new(inputKey, LiteralType,
        ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(LiteralType, JsonSerializer.SerializeToElement(value, LiteralJson), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding RequestBinding(string inputKey, string memberKey) => new(inputKey, LiteralType,
        ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest,
        workflowRequest: new RuntimeWorkflowRequestReference(memberKey));

    private static ExecutableNode JobNode(IReadOnlyDictionary<string, RuntimeInputBinding>? bindings = null,
        IReadOnlyDictionary<string, string>? metadata = null, string nodeId = "job-node") =>
        new(nodeId, "authored-" + nodeId, RunJob.ActivityType, "1.0.0", Descriptor(RunJob.ActivityType),
            bindings ?? Bindings(), metadata ?? new Dictionary<string, string> { ["other-domain"] = "preserved" });

    private static RuntimeActivityDescriptor Descriptor(string activityType) => new("test.clr-activity",
        RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { type = activityType }));

    private static WorkflowExecutableCompileRequest Request() => new("parent-version", WorkflowExecutableReferenceScope.Published,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "artifact-parent-");

    private static WorkflowExecutableCompileSource Source() => new("parent-definition", "parent-version", "1.0.0",
        WorkflowDefinitionState.Empty, "WorkflowDefinitionVersion", "parent-version", "1.0.0");

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
