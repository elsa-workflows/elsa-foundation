using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>How one compiled input of an activity becomes a Native input.</summary>
internal abstract record InputRule
{
    /// <summary>Bound to the Native input of this name, whatever the binding's source.</summary>
    public sealed record Pass(string Native) : InputRule;

    /// <summary>
    /// Bound to the Native input of this name from a literal only, whose value is converted to the Native shape: the old
    /// core and Native type it differently. Convert returns null when the value has no Native form; the refusal then says
    /// what the input takes and ends with <paramref name="Action"/>.
    /// </summary>
    public sealed record Literal(string Native, Func<JsonNode?, JsonNode?> Convert, string Expected, string Action) : InputRule;

    /// <summary>
    /// An input the Native activity does not model. The binding is accepted, and dropped, when it is absent or a literal that
    /// Native behaves as: <see cref="Accepts"/> says so; anything else is refused with the reason and the action.
    /// </summary>
    public sealed record Unmodelled(Func<JsonNode?, bool> Accepts, string Reason, string Action) : InputRule;
}

/// <summary>A Native input bound to a literal when the compiled node leaves it out, because the old core's default differs.</summary>
/// <param name="Applies">Given which compiled inputs the node binds, whether the default applies.</param>
internal sealed record AbsentDefault(string Native, Func<Func<string, bool>, bool> Applies, JsonNode Value);

/// <summary>Where a compiled result projection is in a Native result, or why a read of it is refused.</summary>
/// <param name="Path">The Native path, "" for the whole result; null when only <paramref name="Members"/> may be read.</param>
/// <param name="Members">The members a read may select, by their old names, to their Native names; null when any member may be read as it is named.</param>
/// <param name="Refused">For a projection whose shape differs on this host: why, and what to read instead.</param>
internal sealed record ResultProjection(string? Path, IReadOnlyDictionary<string, string>? Members = null, string? Refused = null);

/// <summary>
/// What the mapper knows about each release 1 activity: how its compiled inputs, keyed as the old core's activity declares
/// them, become Native inputs, the Native inputs an absent one implies, and where its result projections are. An input
/// whose key names a Native input (ignoring case) passes through unless a rule says otherwise; an input with neither is
/// refused, so a behaviour this host does not model is never dropped without a word.
/// </summary>
/// <remarks>
/// Limits, each refused with the node and input named:
/// <list type="bullet">
/// <item>Delay takes a literal duration only, in the form publishing writes a time span (<c>00:00:05</c>,
/// <c>1.00:00:00</c>). Publishing reads an authored bare number such as <c>5</c> as days, as System.Text.Json does, and
/// writes <c>5.00:00:00</c>, so both cores wait five days. A value in any other form is refused.</item>
/// <item>HttpEndpoint takes one HTTP method; Native's endpoint listens on one.</item>
/// <item>HttpEndpoint and Event pass <c>CanStartWorkflow</c> through: false makes them waits, even where a run begins.</item>
/// <item>SendHttpRequest sends its Content as text, text/plain unless its ContentType says otherwise, as the old core does.</item>
/// <item>Result reads whose shapes differ on this host are refused until the shapes match: an HttpEndpoint request's
/// path, headers, query and body, its RouteData (Native lower-cases route value names and joins repeated values),
/// SendHttpRequest's ResponseHeaders and ResponseBody (Native parses the body), and DispatchWorkflow's Result (Native's
/// is the child's output and status, with other status names). An HttpEndpoint's ParsedContent reads the same for a JSON
/// or text body; for a body with no content type the old core reads null and Native the body.</item>
/// </list>
/// </remarks>
internal sealed class NativeActivityShape
{
    private static readonly Dictionary<Type, NativeActivityShape> Shapes = Build();

    private readonly Dictionary<string, string> _nativeInputs;

    private NativeActivityShape(Type type)
    {
        _nativeInputs = type.GetProperties()
            .Where(property => property.IsDefined(typeof(Native.InputAttribute), inherit: true))
            .ToDictionary(property => property.Name, property => property.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Dictionary<string, InputRule> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<AbsentDefault> AbsentDefaults { get; } = [];

    public Dictionary<string, ResultProjection> Results { get; } = new(StringComparer.Ordinal);

    public static NativeActivityShape For(Type type) =>
        Shapes.TryGetValue(type, out var shape) ? shape : throw new InvalidOperationException($"No activity shape for {type.Name}.");

    /// <summary>The rule for a compiled input, or null when the Native activity has no such input.</summary>
    public InputRule? RuleFor(string input) =>
        Rules.TryGetValue(input, out var rule) ? rule : _nativeInputs.TryGetValue(input, out var native) ? new InputRule.Pass(native) : null;

    private static Dictionary<Type, NativeActivityShape> Build()
    {
        var shapes = new Dictionary<Type, NativeActivityShape>();
        NativeActivityShape Add(Type type)
        {
            var shape = new NativeActivityShape(type);
            shapes.Add(type, shape);
            return shape;
        }

        foreach (var type in new[]
                 {
                     typeof(Native.Sequence), typeof(Native.If), typeof(Native.Switch), typeof(Native.For), typeof(Native.While),
                     typeof(Native.Do), typeof(Native.Parallel), typeof(Native.Break), typeof(Native.Flowchart), typeof(Native.Complete),
                     typeof(Native.WriteLine), typeof(Native.WriteLines), typeof(Native.Fault), typeof(Native.WriteHttpResponse),
                 })
            Add(type);

        var forEach = Add(typeof(Native.ForEach));
        forEach.Rules["Collection"] = new InputRule.Pass("Items");
        // Native always declares currentIndex beside currentItem; a body that does not read it is unaffected.
        forEach.Rules["ExposeIndex"] = new InputRule.Unmodelled(_ => true, "", "");

        Add(typeof(Native.Inline)).Results["$result"] = new("");
        Add(typeof(Native.RunJavaScript)).Results["result"] = new("Value");

        var @event = Add(typeof(Native.Event));
        @event.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "this host matches events by name only", "Clear the correlation id.");
        @event.Rules["ExpiresAt"] = new InputRule.Unmodelled(IsNull, "this host has no match deadline for an event wait", "Clear the match deadline.");
        @event.Results["Result"] = new("EventName");
        @event.Results["Payload"] = new("Payload");

        var publish = Add(typeof(Native.PublishEvent));
        publish.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "this host publishes events by name only", "Clear the correlation id.");
        publish.Rules["IsLocalEvent"] = new InputRule.Unmodelled(value => IsNull(value) || IsFalse(value), "this host has no local events", "Turn off Local event.");

        Add(typeof(Native.Delay)).Rules["Duration"] = new InputRule.Literal("Milliseconds", Milliseconds, "a literal time span such as 00:00:05",
            "Use a literal duration.");

        Add(typeof(Native.Timer)).Results["Result"] = new("Interval");
        Add(typeof(Native.Cron)).Results["Result"] = new("Expression");

        var dispatch = Add(typeof(Native.DispatchWorkflow));
        // The definition id as given: the host resolves it to the artifact it serves when the child starts
        // (NativeExecutionOptions.ServedWorkflow).
        dispatch.Rules["WorkflowDefinitionId"] = new InputRule.Pass("Workflow");
        dispatch.Rules["Inputs"] = new InputRule.Pass("Input");
        dispatch.Rules["CancelChildOnParentCancellation"] = new InputRule.Unmodelled(value => IsNull(value) || IsTrue(value),
            "this host always cancels a waited child with its parent", "Turn Cancel child on parent cancellation back on.");
        dispatch.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "this host starts a child without a correlation id", "Clear the correlation id.");
        dispatch.Results["Result"] = new(null, new Dictionary<string, string>(StringComparer.Ordinal),
            "On this host a Dispatch Workflow's result is the child's output and its status, named otherwise, so the old result reads differently. Remove this read until the shapes match.");

        var endpoint = Add(typeof(Native.HttpEndpoint));
        endpoint.Rules["SupportedMethods"] = new InputRule.Literal("Method", OneMethod, "one HTTP method", "Select a single method.");
        endpoint.AbsentDefaults.Add(new("Method", bound => !bound("SupportedMethods"), JsonValue.Create("GET")));
        endpoint.Rules["Authorize"] = new InputRule.Unmodelled(value => IsNull(value) || IsFalse(value), "this host's HTTP edge does not authorize routes", "Turn off Authorize.");
        endpoint.Rules["Policy"] = new InputRule.Unmodelled(IsNull, "this host's HTTP edge does not authorize routes", "Clear the policy.");
        endpoint.Rules["RequestTimeout"] = new InputRule.Unmodelled(IsNull, "this host's reply deadline is the route timeout", "Clear the request timeout.");
        endpoint.Rules["RequestSizeLimit"] = new InputRule.Unmodelled(IsNull, "this host's HTTP edge sets the request size limit", "Clear the request size limit.");
        endpoint.Rules["ResponseMode"] = new InputRule.Unmodelled(value => IsNull(value) || IsSync(value), "this host answers a request from the workflow only", "Set the response mode to Sync.");
        // A request's method reads alike. Its path is the edge's form, its headers and query are lower-cased with repeated
        // values joined, and its body is parsed, so those read differently and are refused.
        endpoint.Results["Request"] = new(null, new Dictionary<string, string>(StringComparer.Ordinal) { ["method"] = "Method" });
        endpoint.Results["RouteData"] = new(null, Refused:
            "On this host route value names are lower-cased, so route data reads differently. Remove this read until the shapes match.");
        endpoint.Results["ParsedContent"] = new("Body");

        var send = Add(typeof(Native.SendHttpRequest));
        send.Rules["Content"] = new InputRule.Pass("Body");
        send.Rules["RequestHeaders"] = new InputRule.Pass("Headers");
        send.Rules["Connection"] = new InputRule.Literal("Connection", ConnectionName, "a literal Connection reference", "Select the Connection directly.");
        send.AbsentDefaults.Add(new("Method", bound => !bound("Method"), JsonValue.Create("GET")));
        // The old Content is text, sent as it is with its ContentType, text/plain by default.
        send.AbsentDefaults.Add(new("ContentType", bound => bound("Content") && !bound("ContentType"), JsonValue.Create("text/plain")));
        send.Results["StatusCode"] = new("StatusCode");
        send.Results["ResponseBody"] = new(null, Refused:
            "On this host the response body is parsed, so it reads differently. Remove this read until this host reads it as text.");
        send.Results["ResponseHeaders"] = new(null, Refused:
            "On this host response headers have one joined value each, so they read differently. Remove this read until the shapes match.");

        // A GraphActivity's inputs and results are its boundary's own (NativeExecutableMapper.MapGraph).
        Add(typeof(Native.GraphActivity)).Results["$result"] = new("");
        return shapes;
    }

    private static bool IsNull(JsonNode? value) => value is null;
    private static bool IsTrue(JsonNode? value) => value is JsonValue v && v.GetValueKind() == JsonValueKind.True;
    private static bool IsFalse(JsonNode? value) => value is JsonValue v && v.GetValueKind() == JsonValueKind.False;

    // ResponseMode serializes as its name or its number; Sync is 1.
    private static bool IsSync(JsonNode? value) => value is JsonValue v &&
        (v.TryGetValue<string>(out var name) && string.Equals(name, "Sync", StringComparison.OrdinalIgnoreCase) || v.TryGetValue<int>(out var number) && number == 1);

    // Publishing writes a time span as System.Text.Json does, [-][d.]hh:mm:ss[.fffffff]; anything else is refused.
    private static JsonNode? Milliseconds(JsonNode? value)
    {
        if (value is not JsonValue v || !v.TryGetValue<string>(out var text) || !text.Contains(':', StringComparison.Ordinal) ||
            !TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var span) || span < TimeSpan.Zero)
            return null;
        return JsonValue.Create((long)span.TotalMilliseconds);
    }

    private static JsonNode? OneMethod(JsonNode? value) =>
        value is JsonArray { Count: 1 } methods && methods[0] is JsonValue method && method.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? JsonValue.Create(name.Trim().ToUpperInvariant())
            : null;

    // A Connection reference is its name, or the reference object the designer stores ({ "connection": name, "type": ... }).
    private static JsonNode? ConnectionName(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) => JsonValue.Create(name),
        JsonObject o when o["connection"] is JsonValue v && v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) => JsonValue.Create(name),
        _ => null,
    };
}
