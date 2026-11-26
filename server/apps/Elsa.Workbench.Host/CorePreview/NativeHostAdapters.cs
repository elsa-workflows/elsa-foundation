using System.Text.Json;
using System.Text.Json.Nodes;
using Cronos;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;

namespace Elsa.Workbench.CorePreview;

/// <summary>
/// RunJavaScript's engine on the kernel host: the old core's isolated Jint evaluator, with its time, statement and recursion
/// limits. The program sees its arguments as frozen <c>args</c>; its value comes back with each number's JSON text kept, so a
/// number names the outcome it names on the old core.
/// </summary>
internal sealed class JintNativeScripts(IJavaScriptScriptEvaluator evaluator) : INativeScripts
{
    public async ValueTask<JsonNode?> EvaluateAsync(string script, JsonObject arguments, CancellationToken cancellationToken)
    {
        var result = await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(script, JsonSerializer.SerializeToElement(arguments), cancellationToken));
        return result is { } value ? JsonNode.Parse(value.GetRawText()) : null;
    }
}

/// <summary>
/// Cron occurrences on the kernel host, read as the old core reads them (Cronos, UTC): five fields are standard cron, six or
/// more include seconds, and an occurrence is the first strictly after the given time.
/// </summary>
internal sealed class CronosCalendar : ICronCalendar
{
    public long? Next(string expression, long after) =>
        Parse(expression).GetNextOccurrence(DateTimeOffset.FromUnixTimeMilliseconds(after), TimeZoneInfo.Utc, inclusive: false)?.ToUnixTimeMilliseconds();

    /// <summary>Why an expression does not read, or null when it does: the check a schedule table runs at registration.</summary>
    public static string? Error(string expression)
    {
        try { _ = Parse(expression); return null; }
        catch (FormatException refused) { return refused.Message; }
    }

    private static CronExpression Parse(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        try { return CronExpression.Parse(expression.Trim(), fields >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard); }
        catch (CronFormatException refused) { throw new FormatException($"Cron expression '{expression}' is invalid: {refused.Message}", refused); }
    }
}
