using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Covers issue #906: conversion-resolution failures during workflow publish/preflight are returned as a
/// structured <c>application/problem+json</c> payload (code VF-COER-001) with the failing node, reference key,
/// contracts, representation, mode, and profile, instead of a plain 400 string.
/// </summary>
public sealed class WorkflowPublicationConversionDiagnosticsTests
{
    private const string VersionId = "workflow-version-1";

    [Fact]
    public void Resolver_threads_binding_context_and_a_stable_reason_code_into_the_rejection()
    {
        var resolver = new ValueConversionPlanResolver();
        var binding = new ValueConversionBindingContext("reader", "amount", ValueConversionBindingKind.Input);

        var exception = Assert.Throws<ValueConversionPublicationException>(() => resolver.Resolve(
            new ValueTypeDescriptor("Int64"),
            ValueRepresentation.TypedValue,
            new ValueTypeDescriptor("Int32"),
            binding: binding));

        Assert.Equal(ValueConversionRejectionReason.AutomaticNumericLossy, exception.ReasonCode);
        Assert.Same(binding, exception.Binding);
        Assert.Equal("reader", exception.Binding!.NodeId);
        Assert.Equal("amount", exception.Binding.ReferenceKey);
        Assert.Equal("Int64", exception.SourceType!.Alias);
        Assert.Equal("Int32", exception.TargetType.Alias);
        Assert.Equal(ValueRepresentation.TypedValue, exception.SourceRepresentation);
        Assert.StartsWith("VF-COER-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Linker_missing_producer_is_a_structured_conversion_failure_that_preserves_the_message()
    {
        var consumer = new ExecutableNode(
            "consumer",
            "consumer",
            "test.consumer",
            "1",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = new(
                    "value",
                    new ValueTypeDescriptor("Int32"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.ActivityResult,
                    activityResult: new RuntimeActivityResultReference("ghost", "value", "root"))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        var root = new ExecutableNode(
            "root",
            "root",
            "test.root",
            "1",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot("activities", [consumer])]);

        var exception = Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(root));

        Assert.Equal(ValueConversionRejectionReason.ProducerNodeMissing, exception.ReasonCode);
        Assert.Equal("consumer", exception.Binding!.NodeId);
        Assert.Equal("value", exception.Binding.ReferenceKey);
        Assert.Equal(ValueConversionBindingKind.ActivityResult, exception.Binding.Kind);
        Assert.Null(exception.SourceType);
        Assert.Contains("is not present in the compiled executable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_endpoint_returns_problem_json_for_an_auto_numeric_narrowing_rejection()
    {
        await using var host = await PublishHost(new ExceptionSender(Wrap(AutoNarrowingRejection())));
        using var response = await PublishAsync(host);

        var problem = await AssertConversionProblemAsync(response);
        var diagnostic = problem.GetProperty("diagnostics")[0];
        Assert.Equal("consumer", diagnostic.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("value", diagnostic.GetProperty("location").GetProperty("referenceKey").GetString());
        var metadata = diagnostic.GetProperty("metadata");
        Assert.Equal("AutomaticNumericLossy", metadata.GetProperty("reasonCode").GetString());
        Assert.Equal("ActivityResult", metadata.GetProperty("bindingKind").GetString());
        Assert.Equal("Auto", metadata.GetProperty("mode").GetString());
        Assert.Equal("Int64/Single/schema:none", metadata.GetProperty("sourceType").GetString());
        Assert.Equal("Int32/Single/schema:none", metadata.GetProperty("targetType").GetString());
        Assert.Equal("TypedValue", metadata.GetProperty("sourceRepresentation").GetString());
        Assert.Equal(VersionId, metadata.GetProperty("workflowVersionId").GetString());
        Assert.StartsWith("VF-COER-001", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_endpoint_returns_problem_json_for_a_none_mode_contract_mismatch()
    {
        var conversion = Assert.Throws<ValueConversionPublicationException>(() => new ValueConversionPlanResolver().Resolve(
            new ValueTypeDescriptor("String"),
            ValueRepresentation.TextValue,
            new ValueTypeDescriptor("Int32"),
            ValueConversionMode.None,
            binding: new ValueConversionBindingContext("mapper", "target", ValueConversionBindingKind.Output)));
        await using var host = await PublishHost(new ExceptionSender(Wrap(conversion)));
        using var response = await PublishAsync(host);

        var problem = await AssertConversionProblemAsync(response);
        var diagnostic = problem.GetProperty("diagnostics")[0];
        Assert.Equal("mapper", diagnostic.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal("target", diagnostic.GetProperty("location").GetProperty("referenceKey").GetString());
        var metadata = diagnostic.GetProperty("metadata");
        Assert.Equal("NoneModeContractMismatch", metadata.GetProperty("reasonCode").GetString());
        Assert.Equal("None", metadata.GetProperty("mode").GetString());
        Assert.Equal("Output", metadata.GetProperty("bindingKind").GetString());
        Assert.False(metadata.TryGetProperty("profileId", out _));
    }

    [Fact]
    public async Task Publish_endpoint_returns_problem_json_for_an_unknown_named_profile()
    {
        var conversion = Assert.Throws<ValueConversionPublicationException>(() => new ValueConversionPlanResolver().Resolve(
            new ValueTypeDescriptor("String"),
            ValueRepresentation.FormattedContent,
            new ValueTypeDescriptor("Customer"),
            ValueConversionMode.Profile,
            new ValueConversionProfileReference("partner.json", "8"),
            binding: new ValueConversionBindingContext("importer", "payload", ValueConversionBindingKind.Input)));
        await using var host = await PublishHost(new ExceptionSender(Wrap(conversion)));
        using var response = await PublishAsync(host);

        var problem = await AssertConversionProblemAsync(response);
        var diagnostic = problem.GetProperty("diagnostics")[0];
        var metadata = diagnostic.GetProperty("metadata");
        Assert.Equal("ProfileNotAvailable", metadata.GetProperty("reasonCode").GetString());
        Assert.Equal("Profile", metadata.GetProperty("mode").GetString());
        Assert.Equal("partner.json", metadata.GetProperty("profileId").GetString());
        Assert.Equal("8", metadata.GetProperty("profileVersion").GetString());
        Assert.Contains("partner.json@8", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_endpoint_returns_a_safe_machine_readable_expression_validation_problem()
    {
        var diagnostic = new ExpressionDiagnostic(
            "JavaScript/Syntax",
            ExpressionDiagnosticSeverity.Error,
            "Unexpected token.",
            "document-revision",
            new(new(1, 2), new(1, 3)),
            "writer/inputs/Text",
            ["private-symbol-id"]);
        var rejection = new ExpressionPublicationValidationException(new(
            ExpressionDraftValidationState.Errors,
            [diagnostic],
            "expression-syntax"));
        await using var host = await PublishHost(new ExceptionSender(rejection));
        using var response = await PublishAsync(host);

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        Assert.Equal("errors", problem.GetProperty("validationState").GetString());
        Assert.Equal("expression-syntax", problem.GetProperty("errorCode").GetString());
        var safeDiagnostic = Assert.Single(problem.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("JavaScript/Syntax", safeDiagnostic.GetProperty("code").GetString());
        Assert.Equal("Error", safeDiagnostic.GetProperty("severity").GetString());
        Assert.Equal("Unexpected token.", safeDiagnostic.GetProperty("message").GetString());
        Assert.Equal("writer/inputs/Text", safeDiagnostic.GetProperty("authoredPath").GetString());
        Assert.False(safeDiagnostic.TryGetProperty("relatedSymbols", out _));
        Assert.False(safeDiagnostic.TryGetProperty("relatedSymbolIds", out _));
        Assert.False(safeDiagnostic.TryGetProperty("metadata", out _));
        Assert.DoesNotContain("private-symbol-id", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preflight_endpoint_produces_the_same_conversion_problem_payload_as_publish()
    {
        var publishConversion = AutoNarrowingRejection();
        var preflightConversion = AutoNarrowingRejection();

        await using var publish = await PublishHost(new ExceptionSender(Wrap(publishConversion)));
        using var publishResponse = await PublishAsync(publish);
        var publishBody = await publishResponse.Content.ReadAsStringAsync();

        await using var preflight = await PublishingMinimalApiScenarioHost.StartAsync(
            compiler: new ThrowingCompiler(Wrap(preflightConversion)));
        using var preflightResponse = await SendPreflightAsync(preflight);
        var preflightBody = await preflightResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, publishResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, preflightResponse.StatusCode);
        Assert.Equal("application/problem+json", preflightResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Normalize(publishBody), Normalize(preflightBody));
    }

    [Fact]
    public async Task Non_conversion_compilation_failures_still_use_the_plain_400_path()
    {
        var exception = new WorkflowExecutableCompilationException(
            "definition-1", VersionId, "Activity node 'a' declares input 'x' more than once.", new ArgumentException("dup"));
        await using var host = await PublishHost(new ExceptionSender(exception));
        using var response = await PublishAsync(host);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("declares input 'x' more than once", body, StringComparison.Ordinal);
        Assert.DoesNotContain("VF-COER-001", body, StringComparison.Ordinal);
    }

    private static ValueConversionPublicationException AutoNarrowingRejection() =>
        Assert.Throws<ValueConversionPublicationException>(() =>
            new ActivityResultConversionPlanLinker(new ValueConversionPlanResolver()).Link(NarrowingTree()));

    private static ExecutableNode NarrowingTree()
    {
        var producer = new ExecutableNode(
            "producer",
            "producer",
            "test.producer",
            "1",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: new ActivityContract(
                "test.producer",
                "1",
                "test.producer",
                JsonSerializer.SerializeToElement(new { }),
                [],
                new ActivityResultContract(
                    new ValueTypeDescriptor("Test.ProducerResult"),
                    isRequired: true,
                    ActivityValuePolicy.Default,
                    [new ActivityResultProjectionContract(
                        "value", "value", new ValueTypeDescriptor("Int64"), true, ActivityValuePolicy.Default, ValueRepresentation.TypedValue)]),
                ["Done"],
                new ActivityActivationRequirement("test.producer", "test")));
        var consumer = new ExecutableNode(
            "consumer",
            "consumer",
            "test.consumer",
            "1",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["value"] = new(
                    "value",
                    new ValueTypeDescriptor("Int32"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.ActivityResult,
                    activityResult: new RuntimeActivityResultReference("producer", "value", "root"))
            },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new ExecutableNode(
            "root",
            "root",
            "test.root",
            "1",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot("activities", [producer, consumer])]);
    }

    private static WorkflowExecutableCompilationException Wrap(ValueConversionPublicationException conversion) =>
        new("definition-1", VersionId, conversion.Message, conversion);

    private static async Task<JsonElement> AssertConversionProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();
        Assert.Equal("VF-COER-001", root.GetProperty("errorCode").GetString());
        Assert.Equal("https://elsa.dev/problems/VF-COER-001", root.GetProperty("type").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("VF-COER-001", root.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Equal("Error", root.GetProperty("diagnostics")[0].GetProperty("severity").GetString());
        return root;
    }

    private static async Task<PublishingMinimalApiHost> PublishHost(IRequestSender sender) =>
        await PublishingMinimalApiHost.StartAsync(_ => sender);

    private static async Task<HttpResponseMessage> PublishAsync(PublishingMinimalApiHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/publishing/workflows/workflow-version-1/publish")
        {
            Content = Json("{\"versionId\":\"body-version\"}")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendPreflightAsync(PublishingMinimalApiScenarioHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/publishing/workflows/workflow-version-1/preflight")
        {
            Content = Json("{\"versionId\":\"body-version\"}")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await host.Client.SendAsync(request);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string Normalize(string body)
    {
        using var document = JsonDocument.Parse(body);
        var map = document.RootElement.EnumerateObject()
            .Where(property => property.Name is not ("traceId" or "instance"))
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(map);
    }

    private sealed class ExceptionSender(Exception exception) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromException<T>(exception);
    }

    private sealed class ThrowingCompiler(Exception exception) : IWorkflowExecutableCompiler
    {
        public ValueTask<WorkflowExecutable> CompileAsync(
            WorkflowExecutableCompileRequest request,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }
}
