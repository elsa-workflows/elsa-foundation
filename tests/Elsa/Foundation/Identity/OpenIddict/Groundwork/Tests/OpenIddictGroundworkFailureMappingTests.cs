using System.Reflection;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.Fixtures;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkFailureMappingTests
{
    [Fact]
    public void Unsupported_generic_delegates_have_one_stable_capability_failure()
    {
        var exception = InvokeMapper(
            "UnsupportedGenericDelegate",
            "IOpenIddictTokenStore.ListAsync<TState,TResult>");

        Assert.Equal(
            "OpenIddictGroundworkCapabilityException",
            exception.GetType().Name);
        Assert.Contains("ListAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_rejection_identifies_the_logical_unit_and_preserves_the_cause()
    {
        var cause = new InvalidOperationException("schema fingerprint is stale");
        var exception = InvokeMapper("Readiness", "openiddict-tokens", cause);

        Assert.Equal(
            "OpenIddictGroundworkReadinessException",
            exception.GetType().Name);
        Assert.Same(cause, exception.InnerException);
        Assert.Contains("openiddict-tokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_propagates_as_the_original_instance()
    {
        var cancellation = new OperationCanceledException("cancelled");

        var mapped = InvokeMapper("Translate", cancellation, "token lookup");

        Assert.Same(cancellation, mapped);
    }

    [Fact]
    public void Concurrency_conflicts_translate_to_openiddict_concurrency_outcomes()
    {
        var exception = InvokeMapper(
            "WriteFailure",
            DocumentStoreWriteResult.ConcurrencyConflict,
            "token-1");

        var concurrency = Assert.IsType<OpenIddictExceptions.ConcurrencyException>(exception);
        Assert.Contains("token-1", concurrency.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_payloads_translate_with_document_context_and_inner_cause()
    {
        var codec = new VersionedJsonDocumentCodec(
            [new DocumentSchemaVersionPolicy("openiddict-token", 1, 1)],
            [],
            new DocumentSchemaVersionFormat(
                static (_, stamp) => int.TryParse(stamp, out var version) ? version : null,
                static (_, version) => version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var corrupt = Assert.Throws<DocumentSchemaVersionException>(() =>
            codec.Deserialize<object>(OpenIddictGroundworkContractFixture.Envelope(
                "openiddict-token",
                "1",
                "{not-json",
                "token-1")));

        var mapped = InvokeMapper("Translate", corrupt, "token decode");

        Assert.Equal(
            "OpenIddictGroundworkSerializationException",
            mapped.GetType().Name);
        Assert.Same(corrupt, mapped.InnerException);
        Assert.Contains("token-1", mapped.Message, StringComparison.Ordinal);
        Assert.Contains("openiddict-token", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_failures_translate_with_operation_context_and_inner_cause()
    {
        var cause = new TimeoutException("provider unavailable");

        var mapped = InvokeMapper("Translate", cause, "application lookup");

        Assert.Equal(
            "OpenIddictGroundworkProviderException",
            mapped.GetType().Name);
        Assert.Same(cause, mapped.InnerException);
        Assert.Contains("application lookup", mapped.Message, StringComparison.Ordinal);
    }

    private static Exception InvokeMapper(string methodName, params object?[] arguments)
    {
        var mapper = OpenIddictGroundworkContractFixture.RequiredType(
            OpenIddictGroundworkContractFixture.FailureMapperTypeName);
        var methods = mapper.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == arguments.Length)
            .ToArray();
        var method = Assert.Single(methods);
        var result = OpenIddictGroundworkContractFixture.Invoke(method, null, arguments);
        return Assert.IsAssignableFrom<Exception>(result);
    }
}
