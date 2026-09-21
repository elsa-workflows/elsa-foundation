using Elsa.Modularity.Api.Endpoints;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// How the apply endpoint publishes each failure. Spec 171 FR-067: an activation guard's refusal is a 409,
/// like the revision conflict beside it, and not the 400 its
/// <see cref="System.InvalidOperationException"/> base would otherwise give it.
/// </summary>
public sealed class ModularityFaultRendererTests
{
    private readonly ModularityFaultRenderer _renderer = new();

    [Fact]
    public async Task AnActivationGuardRefusalIsAConflict()
    {
        var refusal = new FeatureActivationRefusedException([new("SecretsEntityFrameworkCore", "module 'Secrets' has a pending migration")]);

        var (statusCode, body) = await RenderAsync(refusal);

        Assert.Equal(StatusCodes.Status409Conflict, statusCode);
        Assert.Equal(409, body.GetProperty("statusCode").GetInt32());
        Assert.Equal(refusal.Message, body.GetProperty("errors").GetProperty("generalErrors")[0].GetString());
    }

    [Fact]
    public async Task ARevisionConflictIsStillAConflict()
    {
        var (statusCode, _) = await RenderAsync(new FeatureCatalogRevisionConflictException("stale", "current"));

        Assert.Equal(StatusCodes.Status409Conflict, statusCode);
    }

    /// <summary>The arm added for the refusal must not have swallowed its neighbours.</summary>
    [Theory]
    [InlineData(typeof(ArgumentException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status400BadRequest)]
    public async Task OtherFailuresKeepTheirStatus(Type failure, int expected)
    {
        var (statusCode, _) = await RenderAsync((Exception)Activator.CreateInstance(failure, "boom")!);

        Assert.Equal(expected, statusCode);
    }

    private async Task<(int StatusCode, JsonElement Body)> RenderAsync(Exception exception)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        Assert.True(await _renderer.TryWriteAsync(context, exception));

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }
}
