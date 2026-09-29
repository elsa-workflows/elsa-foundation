using Elsa.Primitives.Exceptions;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Buffers;
using System.Text.Json;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// How every first-party domain API answers a <see cref="SchemaWriteRefusedException"/> (spec 180, FR-016a): HTTP 409 in
/// the API's own problem envelope, carrying the refusal's stable code, its family and both versions. Each API owns its
/// envelope and its codes (#2093, Q17); this is the one place the refusal's part of every envelope is decided.
/// </summary>
/// <remarks>
/// <para>
/// Three paths reach it. <see cref="ElsaEndpointsServiceCollectionExtensions.AddElsaEndpoints"/> registers a translator,
/// unkeyed, that turns a refusal into <see cref="For"/>'s problem, so every owner whose fault renderers decline it answers
/// through its own <see cref="IEndpointProblemWriter"/>. A fault renderer that owns a shape end to end answers it in that
/// shape from <see cref="For"/>, or puts <see cref="SchemaWriteRefusedException.Code"/> in the shape's own code member.
/// An operation mapped with <c>containFailures: false</c>, whose failures bypass both, answers it as the problem document
/// <see cref="ElsaEndpointGroupExtensions.MapUnboundOperation"/> writes.
/// </para>
/// <para>
/// The refusal names the family and both versions in its message, so an envelope that shows one message still says what
/// was refused. The code is what a client matches on.
/// </para>
/// </remarks>
public static class SchemaWriteRefusalProblem
{
    /// <summary>The status every API answers a refusal with.</summary>
    public const int StatusCode = StatusCodes.Status409Conflict;

    /// <summary>
    /// The refusal as a problem, its entries in this order: the message under <c>generalErrors</c>, so an envelope that
    /// shows only the first message shows it; then <c>code</c>, <c>family</c>, <c>writeVersion</c> and
    /// <c>requiredVersion</c>, one value each. A dormancy refusal (spec 182, FR-013) adds its caller-neutral
    /// <c>reason</c>, and its <c>feature</c> when it names one.
    /// </summary>
    public static EndpointProblem For(SchemaWriteRefusedException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["generalErrors"] = [refusal.Message],
            ["code"] = [refusal.Code],
            ["family"] = [refusal.Family],
            ["writeVersion"] = [refusal.WriteVersion],
            ["requiredVersion"] = [refusal.RequiredVersion]
        };
        if (refusal is SchemaDormancyRefusedException dormancy)
        {
            if (dormancy.FeatureId is { } feature)
                errors["feature"] = [feature];
            errors["reason"] = [dormancy.Reason];
        }

        return new(StatusCode, errors);
    }

    /// <summary>
    /// Wraps an operation's dispatch so a refusal it raises before the response starts is answered as the problem
    /// document below; every other failure leaves exactly as before.
    /// </summary>
    /// <remarks>
    /// For operations that run outside the failure pipeline, which the host's exception handling would answer with a 500.
    /// The document is the RFC 9457 shape most such owners publish for the failures they handle: type, title, status,
    /// detail and instance, then the problem's entries as <c>errors</c> name/reason pairs and the <c>traceId</c>. Written by
    /// hand, as <see cref="ElsaFallbackEndpointProblemWriter"/> is, so answering the refusal needs no serializer.
    /// </remarks>
    internal static Func<HttpContext, Task> Answering(Func<HttpContext, Task> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return async context =>
        {
            try
            {
                await dispatch(context);
            }
            catch (SchemaWriteRefusedException refusal) when (!context.Response.HasStarted)
            {
                await WriteDocumentAsync(context, refusal);
            }
        };
    }

    private static async Task WriteDocumentAsync(HttpContext context, SchemaWriteRefusedException refusal)
    {
        var problem = For(refusal);
        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/problem+json";
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.8");
            writer.WriteString("title", "Conflict");
            writer.WriteNumber("status", problem.StatusCode);
            writer.WriteString("detail", refusal.Message);
            writer.WriteString("instance", context.Request.Path.Value);
            writer.WriteStartArray("errors");
            foreach (var (name, reasons) in problem.Errors)
            {
                foreach (var reason in reasons)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", name);
                    writer.WriteString("reason", reason);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteString("traceId", context.TraceIdentifier);
            writer.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted);
    }
}

/// <summary>
/// Turns a <see cref="SchemaWriteRefusedException"/> into <see cref="SchemaWriteRefusalProblem.For"/>'s problem for every
/// owner, registered unkeyed by <see cref="ElsaEndpointsServiceCollectionExtensions.AddElsaEndpoints"/>. The pipeline asks
/// it after the owner's own translators, and the owner's problem writer writes what it returns.
/// </summary>
internal sealed class SchemaWriteRefusalTranslator : IEndpointExceptionTranslator
{
    public EndpointProblem? Translate(Exception exception) =>
        exception is SchemaWriteRefusedException refusal ? SchemaWriteRefusalProblem.For(refusal) : null;
}
