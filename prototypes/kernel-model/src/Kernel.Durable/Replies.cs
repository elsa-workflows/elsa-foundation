using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// What an owner sends to a caller that waits: an item a turn emitted, or the end of the observation. <c>Key</c> is the
/// same however often it is sent. An end has no channel: the instance's status, or an error ("lost", "missing").
/// </summary>
public sealed record Reply(string Request, string Key, string? Channel, object? Item, InstanceStatus? Status = null, string? Error = null);

/// <summary>
/// The runtime's transport between nodes for live output. Not durable: a reply may be dropped, delayed or delivered
/// twice. Between the workers of one process it is a call. It rides on the runtime's transport between nodes, but it carries
/// layer 2's replies, so its contract is declared here (FINDINGS section 24).
/// </summary>
public interface IReplyTransport
{
    void Send(string node, Reply reply);
}

/// <summary>
/// A reply address: the node that holds the request, the request's deadline, and the request's id there. It rides on a
/// message as <c>ReplyTo</c>. The deadline is absolute, in database milliseconds, and equal to the route's timeout
/// (decision 6 of 12 November): after it the owner sends this caller nothing and lets go of the instance, which the
/// caller no longer pins in memory. <paramref name="beforeCommit"/> is a route's opt-out of the owner's default of 12
/// November (output is released after the commit of the turn that wrote it): this caller gets a turn's output when the
/// turn ends.
/// </summary>
public static class ReplyTo
{
    private const string Early = "|early";

    public static string Of(string node, string request, long deadline, bool beforeCommit = false) => $"{node}|{deadline}|{request}{(beforeCommit ? Early : "")}";

    public static (string Node, string Request, long Deadline, bool BeforeCommit) Parse(string address)
    {
        var early = address.EndsWith(Early, StringComparison.Ordinal);
        if (early) address = address[..^Early.Length];
        var (node, rest) = address.IndexOf('|') is var at and >= 0 ? (address[..at], address[(at + 1)..]) : ("", "");
        var cut = rest.IndexOf('|');
        return cut < 0 || !long.TryParse(rest[..cut], out var deadline) ? default : (node, rest[(cut + 1)..], deadline, early);
    }
}

/// <summary>
/// One caller's wait: an HTTP request, say. It ends once, with the first response, the first end, or its deadline, and
/// ignores whatever arrives after that, so neither a duplicate nor a late reply can give it a second answer.
/// </summary>
public sealed class Request(string id, long deadline)
{
    public const string Response = "response";      // the channel whose first item answers the request

    public string Id => id;
    public long Deadline => deadline;
    public bool Done { get; private set; }
    public object? Answer { get; private set; }
    public InstanceStatus? Status { get; private set; }
    public string? Error { get; private set; }
    public int Responses { get; private set; }      // accepted; for tests
    public int Ends { get; private set; }

    internal void Receive(Reply reply, bool again)
    {
        if (Done && !again) return;
        if (reply.Channel == Response) (Answer, Responses, Done) = (reply.Item, Responses + 1, true);
        else if (reply.Channel is null) (Status, Error, Ends, Done) = (reply.Status, reply.Error, Ends + 1, true);
    }

    internal void End(string error)
    {
        if (Done) return;
        (Error, Ends, Done) = (error, Ends + 1, true);
    }
}

/// <summary>The open requests of one process. The host gives it what the transport brings and calls <see cref="Expire"/> as time passes.</summary>
public sealed class Requests(Flaw flaws = Flaw.None)
{
    private readonly Dictionary<string, Request> _open = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Request> All => _open.Values;

    public Request Open(string id, long deadline) => _open[id] = new Request(id, deadline);

    public void Receive(Reply reply)
    {
        if (_open.TryGetValue(reply.Request, out var request)) request.Receive(reply, (flaws & Flaw.ReplyTwice) != 0);
    }

    /// <summary>A caller never waits for ever: past its deadline it gets a timeout, whatever happens to the instance.</summary>
    public void Expire(long now)
    {
        if ((flaws & Flaw.NoDeadline) != 0) return;
        foreach (var request in _open.Values)
            if (now >= request.Deadline) request.End("timeout");
    }

    /// <summary>The caller went away. The instance is not told; a route that wants a cancel sends one.</summary>
    public void Disconnect(string id) => _open.GetValueOrDefault(id)?.End("disconnected");
}
