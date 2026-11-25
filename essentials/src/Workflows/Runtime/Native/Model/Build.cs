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
    public static Node Http(string id, Binding url, Binding? body = null, string method = "POST", Binding? headers = null, int[]? expectedStatusCodes = null,
        TimeSpan? timeout = null, string? connection = null)
    {
        var node = Node<SendHttpRequest>(id, ("Url", url), ("Method", method));
        if (body is not null) node.Inputs["Body"] = body;
        if (headers is not null) node.Inputs["Headers"] = headers;
        if (expectedStatusCodes is not null) node.Inputs["ExpectedStatusCodes"] = Binding.Literal(expectedStatusCodes);
        if (timeout is { } t) node.Inputs["Timeout"] = Binding.Literal(t.TotalMilliseconds);
        if (connection is not null) node.Inputs["Connection"] = connection;
        return node;
    }
    public static Node Email(string id, Binding to, Binding subject, Binding body) => Node<SendEmail>(id, ("To", to), ("Subject", subject), ("Body", body));
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

    public static Node Switch(string id, Binding? value, params (SwitchCase Case, Node? Branch)[] cases) => Switch(id, value, null, cases);

    public static Node Switch(string id, Binding? value, Node? @default, params (SwitchCase Case, Node? Branch)[] cases)
    {
        var node = value is null ? Node<Switch>(id) : Node<Switch>(id, ("Value", value));
        foreach (var (@case, branch) in cases)
        {
            node.Cases.Add(@case);
            if (branch is not null) node.Children.Add(branch.In(Native.Switch.CaseSlot(@case.Label)));
        }
        if (@default is not null) node.Children.Add(@default.In(Native.Switch.DefaultSlot));
        return node;
    }

    public static SwitchCase Case(string label, Binding? condition = null) => new(label, condition);

    public static Node For(string id, Binding start, Binding end, Node body, Binding? step = null, bool endInclusive = false)
    {
        var node = Node<For>(id, ("Start", start), ("End", end), ("EndInclusive", endInclusive));
        if (step is not null) node.Inputs["Step"] = step;
        return node.With(body);
    }

    public static Node Do(string id, Binding condition, Node body) => Node<Do>(id, ("Condition", condition)).With(body);
    public static Node Break(string id) => Node<Break>(id);
    public static Node WriteLines(string id, Binding lines) => Node<WriteLines>(id, ("Lines", lines));

    public static Node Fault(string id, Binding? code = null, Binding? message = null, Binding? category = null, Binding? faultType = null)
    {
        var node = Node<Fault>(id);
        if (code is not null) node.Inputs["Code"] = code;
        if (message is not null) node.Inputs["Message"] = message;
        if (category is not null) node.Inputs["Category"] = category;
        if (faultType is not null) node.Inputs["FaultType"] = faultType;
        return node;
    }

    /// <summary>A GraphActivity with its body, its declaration and its inputs bound by name.</summary>
    public static Node GraphActivity(string id, GraphBoundary graph, Node body, params (string Input, Binding Value)[] inputs)
    {
        var node = Node<GraphActivity>(id, inputs).With(body);
        node.Graph = graph;
        return node;
    }

    public static Node Complete(string id, Binding outcome) => Node<Complete>(id, ("Outcome", outcome));

    public static Node Inline(string id, Binding expression) => Node<Inline>(id, ("Expression", expression));

    public static Node RunJavaScript(string id, Binding script, Binding? arguments = null, Binding? possibleOutcomes = null)
    {
        var node = Node<RunJavaScript>(id, ("Script", script));
        if (arguments is not null) node.Inputs["Arguments"] = arguments;
        if (possibleOutcomes is not null) node.Inputs["PossibleOutcomes"] = possibleOutcomes;
        return node;
    }

    public static Node Timer(string id, string interval) => Node<Timer>(id, ("Interval", interval));
    public static Node Cron(string id, string expression) => Node<Cron>(id, ("Expression", expression));

    public static Node SetOutput(string id, string name, Binding value) => Node<SetOutput>(id, ("Name", name), ("Value", value));
    public static Node SetCorrelationId(string id, Binding value) => Node<SetCorrelationId>(id, ("Value", value));
    public static Node SetInstanceName(string id, Binding value) => Node<SetInstanceName>(id, ("Value", value));
    public static Node Finish(string id, string outcome) => Node<Finish>(id, ("Outcome", outcome));

    public static Node ForEach(string id, Binding items, Node body) => Node<ForEach>(id, ("Items", items)).With(body);
    public static Node While(string id, Binding condition, Node body) => Node<While>(id, ("Condition", condition)).With(body);

    public static Node Flowchart(string id, Node[] nodes, Connection[] connections, params (string Node, Gateway Kind)[] gateways)
    {
        var node = Node<Flowchart>(id).With(nodes);
        node.Connections.AddRange(connections);
        foreach (var (child, kind) in gateways) node.Gateways[child] = kind;
        return node;
    }

    public static Node Dispatch(string id, string workflow, bool wait, Binding? input = null, string mode = Modes.Balanced)
    {
        var node = Node<DispatchWorkflow>(id, ("Workflow", workflow), ("WaitForCompletion", wait), ("Mode", mode));
        if (input is not null) node.Inputs["Input"] = input;
        return node;
    }

    public static Node HttpEndpoint(string id, Binding path, string method = "POST", bool respondBeforeCommit = false, Binding? routeValues = null)
    {
        var node = Node<HttpEndpoint>(id, ("Path", path), ("Method", method), ("RespondBeforeCommit", respondBeforeCommit));
        if (routeValues is not null) node.Inputs["RouteValues"] = routeValues;
        return node;
    }

    public static Node Respond(string id, Binding body, int status = 200, string contentType = "text/plain", Binding? headers = null) =>
        headers is null
            ? Node<WriteHttpResponse>(id, ("StatusCode", status), ("ContentType", contentType), ("Body", body))
            : Node<WriteHttpResponse>(id, ("StatusCode", status), ("ContentType", contentType), ("Body", body), ("Headers", headers));

    /// <summary>The same workflow as a version of <paramref name="definitionId"/> (<see cref="Native.Workflow.DefinitionId"/>).</summary>
    public static Workflow VersionOf(string definitionId, Workflow workflow) =>
        new(workflow.Id, workflow.Root, workflow.Variables) { DefinitionId = definitionId };

    public static Workflow Workflow(string id, Node root, params (string Name, object? Value)[] variables) =>
        new(id, root, variables.ToDictionary(v => v.Name, v => Values.From(v.Value)));

    public static JsonObject Object(params (string Name, object? Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (name, value) in fields) o[name] = Values.From(value);
        return o;
    }
}
