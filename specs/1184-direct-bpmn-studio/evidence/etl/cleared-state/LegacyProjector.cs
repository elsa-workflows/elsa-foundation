using System.Text.Json; using Elsa.Workflows.Runtime.Core.Contracts;
public sealed class LegacyProjector : IActivityStateViewProjector {
 public string StateTypeAlias => "old.compiled.state"; public string ViewKind => "old.compiled.view"; public int ViewVersion => 1;
 public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(JsonSerializer.SerializeToElement(new { preserved = true }));
}