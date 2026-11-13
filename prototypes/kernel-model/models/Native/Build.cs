using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>Definitions in code: what a designer would produce, without the designer.</summary>
public static class Wf
{
    public static Node Node<T>(string id, params (string Input, Binding Value)[] inputs) where T : ActivityBase
    {
        var node = new Node(id, typeof(T));
        foreach (var (input, value) in inputs) node.Inputs[input] = value;
        return node;
    }

    public static Node With(this Node node, params Node[] children)
    {
        node.Children.AddRange(children);
        return node;
    }

    public static Node Capture(this Node node, string variable, string path = "")
    {
        node.Captures[variable] = path;
        return node;
    }

    public static Node In(this Node node, string slot)
    {
        node.Slot = slot;
        return node;
    }

    public static Node Sequence(string id, params Node[] children) => Node<Sequence>(id).With(children);
    public static Node WriteLine(string id, Binding text) => Node<WriteLine>(id, ("Text", text));
    public static Node Set(string id, string variable, Binding value) => Node<SetVariable>(id, ("Name", variable), ("Value", value));
    public static Node Notify(string id, Binding message) => Node<SendNotification>(id, ("Message", message));
    public static Node Call(string id, Binding service, long milliseconds = 0) => Node<CallService>(id, ("Service", service), ("Milliseconds", milliseconds));
    public static Node Delay(string id, Binding milliseconds) => Node<Delay>(id, ("Milliseconds", milliseconds));
    public static Node Event(string id, Binding name) => Node<Event>(id, ("EventName", name));
    public static Node Publish(string id, Binding name, Binding? payload = null) =>
        payload is null ? Node<PublishEvent>(id, ("EventName", name)) : Node<PublishEvent>(id, ("EventName", name), ("Payload", payload));

    public static Node If(string id, Binding condition, Node? then = null, Node? @else = null)
    {
        var node = Node<If>(id, ("Condition", condition));
        if (then is not null) node.Children.Add(then.In("Then"));
        if (@else is not null) node.Children.Add(@else.In("Else"));
        return node;
    }

    public static Node Parallel(string id, int? threshold, params Node[] branches) =>
        (threshold is { } t ? Node<Parallel>(id, ("Threshold", t)) : Node<Parallel>(id)).With(branches);

    public static Node ForEach(string id, Binding items, Node body) => Node<ForEach>(id, ("Items", items)).With(body);
    public static Node While(string id, Binding condition, Node body) => Node<While>(id, ("Condition", condition)).With(body);

    public static Node Flowchart(string id, Node[] nodes, Connection[] connections, params (string Node, JoinMode Mode)[] joins)
    {
        var node = Node<Flowchart>(id).With(nodes);
        node.Connections.AddRange(connections);
        foreach (var (target, mode) in joins) node.Joins[target] = mode;
        return node;
    }

    public static Node Dispatch(string id, string workflow, bool wait, Binding? input = null, string mode = Modes.Balanced)
    {
        var node = Node<DispatchWorkflow>(id, ("Workflow", workflow), ("WaitForCompletion", wait), ("Mode", mode));
        if (input is not null) node.Inputs["Input"] = input;
        return node;
    }

    public static Node HttpEndpoint(string id, Binding path, string method = "POST", bool respondBeforeCommit = false) =>
        Node<HttpEndpoint>(id, ("Path", path), ("Method", method), ("RespondBeforeCommit", respondBeforeCommit));

    public static Node Respond(string id, Binding body, int status = 200, string contentType = "text/plain") =>
        Node<WriteHttpResponse>(id, ("StatusCode", status), ("ContentType", contentType), ("Body", body));

    public static Workflow Workflow(string id, Node root, params (string Name, object? Value)[] variables) =>
        new(id, root, variables.ToDictionary(v => v.Name, v => Values.From(v.Value)));

    public static JsonObject Object(params (string Name, object? Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (name, value) in fields) o[name] = Values.From(value);
        return o;
    }
}
