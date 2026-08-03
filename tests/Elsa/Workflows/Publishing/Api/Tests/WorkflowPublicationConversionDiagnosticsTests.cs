using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Services;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
        var conversion = AutoNarrowingRejection();
        var endpoint = PublishEndpoint(new ExceptionSender(Wrap(conversion)));

        await InvokePublishAsync(endpoint);

        var problem = await AssertConversionProblemAsync(endpoint);
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
        var endpoint = PublishEndpoint(new ExceptionSender(Wrap(conversion)));

        await InvokePublishAsync(endpoint);

        var problem = await AssertConversionProblemAsync(endpoint);
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
        var endpoint = PublishEndpoint(new ExceptionSender(Wrap(conversion)));

        await InvokePublishAsync(endpoint);

        var problem = await AssertConversionProblemAsync(endpoint);
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
        var endpoint = PublishEndpoint(new ExceptionSender(rejection));

        await InvokePublishAsync(endpoint);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", endpoint.HttpContext.Response.ContentType);
        var body = await BodyAsync(endpoint);
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

        var publish = PublishEndpoint(new ExceptionSender(Wrap(publishConversion)));
        await InvokePublishAsync(publish);
        var publishBody = await BodyAsync(publish);

        var preflight = PreflightEndpoint(new ThrowingCompiler(Wrap(preflightConversion)));
        await InvokePreflightAsync(preflight);
        var preflightBody = await BodyAsync(preflight);

        Assert.Equal(StatusCodes.Status400BadRequest, publish.HttpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, preflight.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", preflight.HttpContext.Response.ContentType);
        Assert.Equal(Normalize(publishBody), Normalize(preflightBody));
    }

    [Fact]
    public async Task Non_conversion_compilation_failures_still_use_the_plain_400_path()
    {
        var endpoint = PublishEndpoint(new ExceptionSender(new WorkflowExecutableCompilationException(
            "definition-1", VersionId, "Activity node 'a' declares input 'x' more than once.", new ArgumentException("dup"))));

        // A non-conversion ArgumentException must keep flowing through ThrowError (which signals FastEndpoints'
        // plain 400) instead of being rewritten as the VF-COER-001 problem+json payload.
        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() => InvokePublishAsync(endpoint));

        Assert.Contains("declares input 'x' more than once", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("VF-COER-001", failure.Message, StringComparison.Ordinal);
        Assert.NotEqual("application/problem+json", endpoint.HttpContext.Response.ContentType);
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

    private static async Task<JsonElement> AssertConversionProblemAsync(BaseEndpoint endpoint)
    {
        Assert.Equal(StatusCodes.Status400BadRequest, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", endpoint.HttpContext.Response.ContentType);
        using var document = JsonDocument.Parse(await BodyAsync(endpoint));
        var root = document.RootElement.Clone();
        Assert.Equal("VF-COER-001", root.GetProperty("errorCode").GetString());
        Assert.Equal("https://elsa.dev/problems/VF-COER-001", root.GetProperty("type").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("VF-COER-001", root.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Equal("Error", root.GetProperty("diagnostics")[0].GetProperty("severity").GetString());
        return root;
    }

    private static string Normalize(string body)
    {
        using var document = JsonDocument.Parse(body);
        // The trace identifier is per-request; strip it so the payload comparison is deterministic.
        var map = document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(map);
    }

    private static BaseEndpoint PublishEndpoint(IRequestSender sender) =>
        CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PublishWorkflowEndpoint",
            [sender, NullLoggerFor("PublishWorkflowEndpoint")]);

    private static BaseEndpoint PreflightEndpoint(IWorkflowExecutableCompiler compiler) =>
        CreateEndpoint(
            "Elsa.Workflows.Publishing.Api.Endpoints.PreflightWorkflowPublicationEndpoint",
            [
                compiler,
                RuntimeHelpers_UninitializedPreflightReader(),
                TimeProvider.System,
                NullLoggerFor("PreflightWorkflowPublicationEndpoint")
            ]);

    private static Task InvokePublishAsync(BaseEndpoint endpoint) =>
        InvokeHandleAsync(endpoint, new PublishWorkflowRequest(VersionId));

    private static Task InvokePreflightAsync(BaseEndpoint endpoint) =>
        InvokeHandleAsync(endpoint, new Elsa.Workflows.Publishing.Api.Requests.PreflightWorkflowPublication(VersionId));

    private static BaseEndpoint CreateEndpoint(string typeName, object[] dependencies)
    {
        var endpointType = typeof(PublishWorkflowRequest).Assembly.GetType(typeName, throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var second] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) && second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        Action<DefaultHttpContext> configure = context => context.Response.Body = new MemoryStream();
        return (BaseEndpoint)create.Invoke(null, [configure, dependencies])!;
    }

    private static Task InvokeHandleAsync<TRequest>(BaseEndpoint endpoint, TRequest request) =>
        (Task)endpoint.GetType().GetMethod("HandleAsync", [typeof(TRequest), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private static async Task<string> BodyAsync(BaseEndpoint endpoint)
    {
        endpoint.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(endpoint.HttpContext.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static object NullLoggerFor(string endpointTypeName)
    {
        var endpointType = typeof(PublishWorkflowRequest).Assembly
            .GetType($"Elsa.Workflows.Publishing.Api.Endpoints.{endpointTypeName}", throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        return loggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
               ?? loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null)!;
    }

    private static WorkflowPublicationPreflightReader RuntimeHelpers_UninitializedPreflightReader() =>
        (WorkflowPublicationPreflightReader)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(WorkflowPublicationPreflightReader));

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
