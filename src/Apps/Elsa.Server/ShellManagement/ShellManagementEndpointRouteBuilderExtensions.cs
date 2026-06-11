using CShells.Lifecycle;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Elsa.Server.ShellManagement;

internal static class ShellManagementEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapShellManagementApi(this IEndpointRouteBuilder endpoints, string prefix = "/_admin/shells")
    {
        var group = endpoints.MapGroup(prefix);
        group.MapPost("/reload/{name}", ReloadShell);
        group.MapPost("/reload-all", ReloadAllShells);
        return group;
    }

    private static async Task<Ok<ReloadResultResponse>> ReloadShell(
        string name,
        IShellRegistry registry,
        CancellationToken cancellationToken)
    {
        var result = await registry.ReloadAsync(name, cancellationToken);
        return TypedResults.Ok(ReloadResultResponse.From(result));
    }

    private static async Task<Results<Ok<ReloadResultResponse[]>, BadRequest<string>>> ReloadAllShells(
        HttpRequest request,
        IShellRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!TryReadMaxDegreeOfParallelism(request, out var maxDegreeOfParallelism, out var error))
            return TypedResults.BadRequest(error);

        var options = maxDegreeOfParallelism is null
            ? new ReloadOptions()
            : new ReloadOptions(maxDegreeOfParallelism.Value);
        var results = await registry.ReloadActiveAsync(options, cancellationToken);

        return TypedResults.Ok(results.Select(ReloadResultResponse.From).ToArray());
    }

    private static bool TryReadMaxDegreeOfParallelism(HttpRequest request, out int? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (!request.Query.TryGetValue("maxDegreeOfParallelism", out var raw) || string.IsNullOrWhiteSpace(raw))
            return true;

        if (!int.TryParse(raw, out var parsed))
        {
            error = "maxDegreeOfParallelism must be an integer.";
            return false;
        }

        if (parsed is < ReloadOptions.MinParallelism or > ReloadOptions.MaxParallelism)
        {
            error = $"maxDegreeOfParallelism must be between {ReloadOptions.MinParallelism} and {ReloadOptions.MaxParallelism}.";
            return false;
        }

        value = parsed;
        return true;
    }

    private sealed record ReloadResultResponse(
        string Name,
        bool Success,
        ShellGenerationResponse? NewShell,
        ErrorDescription? Error)
    {
        public static ReloadResultResponse From(ReloadResult result) =>
            new(
                result.Name,
                result.Error is null && result.NewShell is not null,
                result.NewShell is null ? null : ShellGenerationResponse.From(result.NewShell),
                result.Error is null ? null : new ErrorDescription(result.Error.GetType().Name, result.Error.Message));
    }

    private sealed record ShellGenerationResponse(
        string Name,
        int Generation,
        string State,
        DateTimeOffset CreatedAt)
    {
        public static ShellGenerationResponse From(IShell shell) =>
            new(shell.Descriptor.Name, shell.Descriptor.Generation, shell.State.ToString(), shell.Descriptor.CreatedAt);
    }

    private sealed record ErrorDescription(string Type, string Message);
}
