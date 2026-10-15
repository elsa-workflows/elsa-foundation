using Elsa3.Mapping.Resolution;
using static Elsa3.Models.Elsa3ActivityDiagnosticCodes;

namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// The table. Each section mirrors a section of <c>specs/236-elsa3-activity-map/member-map.md</c>; member names are the
/// Elsa 3 C# member names (JSON is camelCase and matched case-insensitively), keys are the exact Elsa 4 keys.
/// </summary>
internal sealed partial class Elsa3BuiltInActivityMap
{
    private const string If = "Elsa.Activities.If.Activities.If";
    private const string Switch = "Elsa.Activities.Switch.Activities.Switch";
    private const string For = "Elsa.Activities.For.Activities.For";
    private const string ForEach = "Elsa.Activities.ForEach.Activities.ForEach";
    private const string While = "Elsa.Activities.While.Activities.While";
    private const string Parallel = "Elsa.Activities.Parallel.Activities.Parallel";
    private const string Sequence = "Elsa.Activities.Sequence.Activities.Sequence";
    private const string Primitives = "Elsa.Activities.Primitives.Activities.";
    private const string Scheduling = "Elsa.Activities.Scheduling.Activities.";
    private const string Http = "Elsa.Activities.Http.Activities.";
    private const string DispatchWorkflow = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow";
    private const string RunJavaScript = "Elsa.Activities.Scripting.Activities.RunJavaScript";

    private static IEnumerable<Elsa3ActivityMapEntry> BuildEntries() =>
        ControlFlow()
            .Concat(FlowModel())
            .Concat(PrimitivesAndIntrinsics())
            .Concat(HttpActivities())
            .Concat(SchedulingActivities())
            .Concat(RuntimeActivities())
            .Concat(JavaScript());

    private static IEnumerable<Elsa3ActivityMapEntry> ControlFlow() =>
    [
        Catalog("If", If,
            [Carry("Condition", "Condition"), Slot("Then", "If.Then"), Slot("Else", "If.Else"), Lost("Result")],
            [Outcome("Done", "True", "False")]),
        Catalog("While", While,
            [Carry("Condition", "Condition", WhilePersistedCondition), Slot("Body", "While.Body")]),
        Catalog("For", For,
            [
                Carry("Start", "Start"),
                Carry("End", "End"),
                Carry("Step", "Step", ForZeroStepFaults),
                DefaultFlip("OuterBoundInclusive", "EndInclusive", "true"),
                Slot("Body", "For.Body"),
                Lost("CurrentValue")
            ],
            rewrite: Elsa3NodeRewriteKind.LoopVariableReferences),
        Catalog("ForEach", ForEach,
            [Carry("Items", "Collection"), Slot("Body", "ForEach.Body"), Lost("CurrentValue")],
            rewrite: Elsa3NodeRewriteKind.LoopVariableReferences),
        NoCounterpart("ParallelForEach", "Elsa 4 has no parallel for-each.",
            Removed("Items"), Removed("Body", kind: Elsa3MemberKind.Port)),
        Catalog("Sequence", Sequence,
            [Slot("Activities", "Sequence.Activities"), Structure("Variables")]),
        Catalog("Parallel", Parallel,
            [Slot("Activities", "Parallel.Branch"), Removed("Variables", kind: Elsa3MemberKind.Property)],
            rewrite: Elsa3NodeRewriteKind.ParallelBranchNames),
        Catalog("Fork", Parallel,
            [Slot("Branches", "Parallel.Branch"), Structure("JoinMode", Elsa3MemberKind.Input, ForkWaitAnyCancellation)],
            rewrite: Elsa3NodeRewriteKind.ParallelBranchNames),
        Catalog("Switch", Switch,
            [Structure("Cases"), Slot("Default", "Switch.Default"), Structure("Mode", Elsa3MemberKind.Input), Lost("Output")],
            [Outcome("Done", "{match}", "Default")],
            Elsa3NodeRewriteKind.SwitchValueSynthesis),
        GraphLevel("Flowchart", "a native Elsa 4 Flowchart needs its connections and start node.",
            Slot("Activities", "Flowchart.Activities"),
            Structure("Variables"),
            Graph("Connections"),
            Graph("Start", Elsa3MemberKind.Port)),
        Catalog("Break", Primitives + "Break", [], [], null, BreakStopsAtParallel),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> FlowModel() =>
    [
        Catalog("FlowDecision", If,
            [Carry("Condition", "Condition")],
            [Outcome("True", "True"), Outcome("False", "False")]),
        Catalog("FlowSwitch", Switch,
            [Structure("Cases"), Structure("Mode", Elsa3MemberKind.Input)],
            [Outcome("Default", "Default")],
            Elsa3NodeRewriteKind.SwitchValueSynthesis),
        GraphLevel("FlowFork", "a fork is a Flowchart graph construct.", Graph("Branches", Elsa3MemberKind.Input)),
        GraphLevel("FlowJoin", "a join is a per-node Flowchart join policy.", Graph("Mode", Elsa3MemberKind.Input)),
        GraphLevel("Start", "the start node becomes the Flowchart's start node id."),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> PrimitivesAndIntrinsics() =>
    [
        Catalog("WriteLine", Primitives + "WriteLine", [Carry("Text", "text")]),
        Catalog("ReadLine", Primitives + "ReadLine", [CarryOutput("Result", "Result")]),
        Catalog("Fault", Primitives + "Fault",
            [
                DefaultFlip("Code", "code", "0"),
                DefaultFlip("Category", "category", "General"),
                DefaultFlip("FaultType", "faultType", "System"),
                Carry("Message", "message")
            ]),
        Intrinsic("SetVariable", Elsa3IntrinsicContracts.Set,
            [Structure("Variable", Elsa3MemberKind.Input), Carry("Value", Elsa3IntrinsicContracts.ValueKey)],
            Elsa3NodeRewriteKind.VariableReference),
        Intrinsic("SetOutput", Elsa3IntrinsicContracts.SetOutput,
            [
                Narrow("OutputName", Elsa3IntrinsicContracts.NameKey, Elsa3ValueContract.LiteralOnly),
                Carry("OutputValue", Elsa3IntrinsicContracts.ValueKey)
            ],
            Elsa3NodeRewriteKind.ReusableOutputWrite),
        Intrinsic("Correlate", Elsa3IntrinsicContracts.SetCorrelationId,
            [Carry("CorrelationId", Elsa3IntrinsicContracts.ValueKey)]),
        Intrinsic("SetName", Elsa3IntrinsicContracts.SetInstanceName,
            [Carry("Value", Elsa3IntrinsicContracts.ValueKey)]),
        Intrinsic("Finish", Elsa3IntrinsicContracts.Finish,
            [Synthetic(Elsa3IntrinsicContracts.OutcomeKey, "Done")]),
        Intrinsic("Complete", Elsa3IntrinsicContracts.Finish,
            [Structure("Outcomes", Elsa3MemberKind.Input)],
            Elsa3NodeRewriteKind.TerminalPlacement),
        Intrinsic("End", Elsa3IntrinsicContracts.Finish, [], Elsa3NodeRewriteKind.TerminalPlacement),
        NoCounterpart("Inline", "inline .NET code does not run in Elsa 4."),
        NoCounterpart("DynamicActivity", "dynamic activities have no Elsa 4 counterpart."),
        NoCounterpart("NotFoundActivity", "Elsa 3 could not resolve this activity's type."),
        NoCounterpart("StateMachine", "Elsa 4 has no state machine."),
        NoCounterpart("Workflow", "a workflow-as-activity reference is a reusable reference, handled by the collection plan."),
        NoCounterpart("CompositeWithResult", "a composite activity is a reusable reference, handled by the collection plan."),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> HttpActivities() =>
    [
        Catalog("HttpEndpoint", Http + "HttpEndpoint",
            [
                Narrow("Path", "Path", Elsa3ValueContract.LiteralOnly),
                Carry("SupportedMethods", "SupportedMethods"),
                Carry("Authorize", "Authorize"),
                Carry("Policy", "Policy"),
                Carry("RequestTimeout", "RequestTimeout"),
                Carry("RequestSizeLimit", "RequestSizeLimit", RequestSizeNoOutcome),
                Removed("FileSizeLimit"),
                Removed("AllowedFileExtensions"),
                Removed("BlockedFileExtensions"),
                Removed("AllowedMimeTypes"),
                Removed("ExposeRequestTooLargeOutcome", "false"),
                Removed("ExposeFileTooLargeOutcome", "false"),
                Removed("ExposeInvalidFileExtensionOutcome", "false"),
                Removed("ExposeInvalidFileMimeTypeOutcome", "false"),
                Lost("Result", "Request"),
                CarryOutput("ParsedContent", "ParsedContent", FormPostNotParsed),
                CarryOutput("RouteData", "RouteData"),
                Lost("QueryStringData", "Request.Query"),
                Lost("Headers", "Request.Headers"),
                Lost("Files"),
                Lost("File"),
                CanStartWorkflowCarried(),
                Synthetic("ResponseMode", "Sync")
            ],
            [
                UnsupportedOutcome("Request too large", "Elsa 4 has no request-size outcome."),
                UnsupportedOutcome("File too large", "Elsa 4 does not validate uploaded files."),
                UnsupportedOutcome("Invalid file extension", "Elsa 4 does not validate uploaded files."),
                UnsupportedOutcome("Invalid file MIME type", "Elsa 4 does not validate uploaded files.")
            ]),
        SendHttpRequest("SendHttpRequest", flow: false),
        SendHttpRequest("FlowSendHttpRequest", flow: true),
        Catalog("WriteHttpResponse", Http + "WriteHttpResponse",
            [
                Narrow("StatusCode", "StatusCode", Elsa3ValueContract.EnumNameToInt),
                Narrow("Content", "Body", Elsa3ValueContract.StringOnly),
                Carry("ContentType", "ContentType"),
                Carry("ResponseHeaders", "Headers")
            ]),
        NoCounterpart("DownloadHttpFile", "Elsa 4 has no file download activity."),
        NoCounterpart("WriteFileHttpResponse", "Elsa 4 has no file response activity."),
    ];

    private static Elsa3ActivityMapEntry SendHttpRequest(string elsa3Type, bool flow)
    {
        var members = new List<Elsa3MemberRule>
        {
            Carry("Url", "Url"),
            Narrow("Method", "Method", Elsa3ValueContract.HttpMethodSupported),
            Narrow("Content", "Content", Elsa3ValueContract.StringOnly),
            DefaultFlip("ContentType", "ContentType", "application/json", whenBound: "Content"),
            Narrow("Authorization", "Authorization", Elsa3ValueContract.SecretReferenceOnly),
            Removed("DisableAuthorizationHeaderValidation", "true", warning: AuthHeaderUnvalidated),
            Narrow("RequestHeaders", "RequestHeaders", Elsa3ValueContract.SingleHeaderValue),
            Removed("EnableResiliency", "false"),
            CarryOutput("StatusCode", "StatusCode"),
            SemanticsChanged("ParsedContent"),
            CarryOutput("ResponseHeaders", "ResponseHeaders"),
            Lost("Result")
        };
        if (flow)
        {
            members.Add(Carry("ExpectedStatusCodes", "ExpectedStatusCodes"));
        }
        else
        {
            members.Add(UseRestriction("ExpectedStatusCodes", null));
            members.Add(UseRestriction("UnmatchedStatusCode", null));
            members.Add(UseRestriction("FailedToConnect", null));
            members.Add(UseRestriction("Timeout", null));
        }

        return Catalog(elsa3Type, Http + "SendHttpRequest",
            members.ToArray(),
            [
                Outcome("Done", "Done"),
                Outcome("Failed to connect", "Failed"),
                Outcome("Timeout", "Timeout"),
                Outcome("Unmatched status code", "Unmatched status code")
            ],
            null,
            HttpOutcomeSingle);
    }

    private static IEnumerable<Elsa3ActivityMapEntry> SchedulingActivities() =>
    [
        Catalog("Delay", Scheduling + "Delay", [DefaultFlip("TimeSpan", "Duration", "00:01:00")]),
        Catalog("Timer", Scheduling + "Timer",
            [Narrow("Interval", "Interval", Elsa3ValueContract.LiteralTimeSpanToString, "00:01:00"), StartOnly()]),
        Catalog("Cron", Scheduling + "Cron",
            [Narrow("CronExpression", "Expression", Elsa3ValueContract.LiteralOnly), StartOnly()]),
        NoCounterpart("StartAt", "Elsa 4 has no start-at trigger."),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> RuntimeActivities() =>
    [
        Catalog("Event", Primitives + "Event",
            [
                Carry("EventName", "EventName"),
                Lost("Result"),
                new Elsa3MemberRule("CanStartWorkflow", Elsa3MemberKind.CustomProperty, Elsa3MemberRuleKind.DefaultFlip, "CanStartWorkflow", Literal("false"))
            ]),
        Catalog("PublishEvent", Primitives + "PublishEvent",
            [
                Carry("EventName", "EventName"),
                Carry("CorrelationId", "CorrelationId"),
                Carry("IsLocalEvent", "IsLocalEvent"),
                Narrow("Payload", "Payload", Elsa3ValueContract.JsonOnly)
            ]),
        Catalog("DispatchWorkflow", DispatchWorkflow, DispatchMembers(executeWorkflow: false),
            [Outcome("Done", "Dispatched", "Completed")]),
        Catalog("ExecuteWorkflow", DispatchWorkflow, DispatchMembers(executeWorkflow: true),
            [Outcome("Done", "Completed")],
            Elsa3NodeRewriteKind.ExecuteWorkflowAsDispatch,
            ExecuteWorkflowDurable),
        NoCounterpart("BulkDispatchWorkflows", "Elsa 4 has no bulk dispatch."),
        NoCounterpart("RunTask", "Elsa 4 has no run-task activity."),
    ];

    private static Elsa3MemberRule[] DispatchMembers(bool executeWorkflow)
    {
        var members = new List<Elsa3MemberRule>
        {
            Narrow("WorkflowDefinitionId", "WorkflowDefinitionId", Elsa3ValueContract.LiteralOnly),
            Carry("CorrelationId", "CorrelationId"),
            Narrow("Input", "Inputs", Elsa3ValueContract.JsonOnly),
            Lost("Result"),
            Synthetic("CancelChildOnParentCancellation", "false")
        };
        if (executeWorkflow)
        {
            members.Add(DefaultFlip("WaitForCompletion", "WaitForCompletion", "true"));
        }
        else
        {
            members.Add(Carry("WaitForCompletion", "WaitForCompletion"));
            members.Add(Removed("StartNewTrace", "false"));
            members.Add(Removed("ChannelName"));
        }

        return members.ToArray();
    }

    private static IEnumerable<Elsa3ActivityMapEntry> JavaScript() =>
    [
        Catalog("RunJavaScript", RunJavaScript,
            [
                Carry("Script", "script"),
                Carry("PossibleOutcomes", "possibleOutcomes", ScriptOutcomeRouting),
                CarryOutput("Result", "result")
            ]),
    ];
}
