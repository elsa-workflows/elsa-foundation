using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Extracts the provider's actual MongoDB command from a retained explain response. MongoDB explain
/// responses carry the command under a nested <c>command</c> property; the command-observer event in
/// Groundwork preview.8 is intentionally descriptive and cannot replace this evidence.
/// </summary>
internal static class MongoExplainCommandInspector
{
    internal static JsonElement ExtractAggregateCommand(string rawPlan)
    {
        var commands = ExtractCommands(rawPlan);
        if (commands.Count != 1)
            throw new PerformanceContractException(
                $"MongoDB native plan must retain exactly one actual aggregate command and pipeline; observed {commands.Count}.");

        var command = commands[0];
        if (!command.TryGetProperty("aggregate", out _) ||
            !command.TryGetProperty("pipeline", out _))
            throw new PerformanceContractException(
                "MongoDB native plan must retain one actual aggregate command and pipeline.");
        return command;
    }

    internal static JsonElement ExtractCommand(string rawPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPlan);
        try
        {
            var commands = ExtractCommands(rawPlan);
            if (commands.Count != 1)
                throw new PerformanceContractException(
                    $"MongoDB native plan must retain exactly one actual aggregate/find command; observed {commands.Count}.");
            return commands[0];
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"MongoDB native plan is not valid explain JSON: {exception.Message}");
        }
    }

    internal static JsonElement ParseCommandText(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        try
        {
            using var document = JsonDocument.Parse(commandText);
            if (!IsCommand(document.RootElement))
                throw new PerformanceContractException(
                    "MongoDB command text must be one actual aggregate or find command.");
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"MongoDB command text is not valid JSON: {exception.Message}");
        }
    }

    internal static string SerializeCommand(JsonElement rawPlan) =>
        JsonSerializer.Serialize(rawPlan);

    internal static void RequireCommandMatchesExplain(string commandText, string rawPlan)
    {
        var expected = ExtractCommand(rawPlan);
        var actual = ParseCommandText(commandText);
        if (!Equivalent(actual, expected))
            throw new PerformanceContractException(
                "MongoDB command text does not match the actual command retained in its explain response.");
    }

    private static void CollectCommands(JsonElement value, List<JsonElement> commands)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("command", out var command) &&
                command.ValueKind == JsonValueKind.Object &&
                IsCommand(command))
                commands.Add(command.Clone());

            foreach (var property in value.EnumerateObject())
                CollectCommands(property.Value, commands);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                CollectCommands(item, commands);
        }
    }

    private static IReadOnlyList<JsonElement> ExtractCommands(string rawPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPlan);
        try
        {
            using var document = JsonDocument.Parse(rawPlan);
            var commands = new List<JsonElement>();
            CollectCommands(document.RootElement, commands);
            return commands;
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException(
                $"MongoDB native plan is not valid explain JSON: {exception.Message}");
        }
    }

    private static bool IsCommand(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        ((value.TryGetProperty("aggregate", out var aggregate) &&
          aggregate.ValueKind == JsonValueKind.String &&
          !string.IsNullOrWhiteSpace(aggregate.GetString()) &&
          value.TryGetProperty("pipeline", out var pipeline) &&
          pipeline.ValueKind == JsonValueKind.Array) ||
         (value.TryGetProperty("find", out var find) &&
          find.ValueKind == JsonValueKind.String &&
          !string.IsNullOrWhiteSpace(find.GetString()) &&
          value.TryGetProperty("filter", out var filter) &&
          filter.ValueKind == JsonValueKind.Object));

    private static bool Equivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;
        return left.ValueKind switch
        {
            JsonValueKind.Object =>
                left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(property =>
                    right.TryGetProperty(property.Name, out var counterpart) &&
                    Equivalent(property.Value, counterpart)),
            JsonValueKind.Array =>
                left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Equivalent(pair.First, pair.Second)) &&
                left.GetArrayLength() == right.GetArrayLength(),
            _ => left.GetRawText() == right.GetRawText()
        };
    }
}
