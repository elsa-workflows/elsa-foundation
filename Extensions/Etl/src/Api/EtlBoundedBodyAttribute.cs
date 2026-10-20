using System.Buffers;
using Elsa.Etl.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Etl.Api;

/// <summary>Bounds the entire authoring envelope before typed JSON binding, including chunked requests.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class EtlBoundedBodyAttribute(int maximumBodyBytes = EtlArtifact.MaximumByteLength + 4096) : Attribute, IEndpointConventionAttribute
{
    internal const int MaximumBodyBytes = EtlArtifact.MaximumByteLength + 4096;

    public void Apply(IEndpointConventionBuilder builder) => builder.Add(endpoint =>
    {
        var next = endpoint.RequestDelegate ?? throw new InvalidOperationException("An ETL endpoint requires a request delegate.");
        endpoint.RequestDelegate = async context =>
        {
            if (context.Request.ContentLength > maximumBodyBytes)
            {
                await EtlProblemWriter.WriteAsync(context, 413, "etl.request-too-large");
                return;
            }

            var original = context.Request.Body;
            await using var body = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int read;
                while ((read = await original.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBodyBytes + 1 - (int)body.Length)), context.RequestAborted)) != 0)
                {
                    if (body.Length + read > maximumBodyBytes)
                    {
                        await EtlProblemWriter.WriteAsync(context, 413, "etl.request-too-large");
                        return;
                    }
                    body.Write(buffer, 0, read);
                }
                body.Position = 0;
                context.Request.Body = body;
                await next(context);
            }
            finally
            {
                context.Request.Body = original;
                ArrayPool<byte>.Shared.Return(buffer);
            }
        };
    });
}
