using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>
/// A fire-and-forget HTTP send for the tests: an External leaf with one outbound effect, its effect key in the
/// <c>Idempotency-Key</c> header, as <see cref="SendHttpRequest"/> sent it before it became a background job whose response
/// completes the activity (design card 2). The kernel's effect-key witnesses (the native simulation, the variant and
/// replay checks, the tenant and statechart witnesses) keep exercising an External effect through it.
/// </summary>
[External]
public sealed class PostHttp : Activity
{
    [Input] public string Url { get; set; } = "";
    [Input] public string Method { get; set; } = "POST";
    [Input] public JsonNode? Body { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        var request = new OutboundHttpRequest(Method, Url, Body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [SendHttpRequest.IdempotencyKeyHeader] = context.EffectKey });
        return new(Complete().With(new HttpSendEffect(request)));
    }
}

/// <summary>The tests' builder for <see cref="PostHttp"/>.</summary>
public static class TestWf
{
    public static Node Post(string id, Binding url, Binding? body = null, string method = "POST") =>
        body is null ? Wf.Node<PostHttp>(id, ("Url", url), ("Method", method)) : Wf.Node<PostHttp>(id, ("Url", url), ("Method", method), ("Body", body));
}
