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
    private const string Flowchart = "Elsa.Activities.Flowchart.Activities.Flowchart";
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
            [Carry("Condition", "Condition") with { OnNull = NullUnbound }, Slot("Then", "If.Then"), Slot("Else", "If.Else"), Lost("Result")],
            [Outcome("Done", "True", "False")]),
        Catalog("While", While,
            [Carry("Condition", "Condition", WhilePersistedCondition) with { OnNull = NullUnbound }, Slot("Body", "While.Body")]),
        Catalog("For", For,
            [
                Carry("Start", "Start") with { OnNull = NullUnbound },
                Carry("End", "End") with { OnNull = NullUnbound },
                Carry("Step", "Step", ForZeroStepFaults) with { OnNull = NullRefused("Elsa 3 ran the loop with a step of 0, so it never advanced.") },
                DefaultFlip("OuterBoundInclusive", "EndInclusive", "true"),
                Slot("Body", "For.Body"),
                Lost("CurrentValue")
            ],
            rewrite: Elsa3NodeRewriteKind.LoopVariableReferences),
        Catalog("ForEach", ForEach,
            [Carry("Items", "Collection") with { OnNull = NullRefused("Elsa 3 faults on a null collection.") }, Slot("Body", "ForEach.Body"), Lost("CurrentValue")],
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
        // Connections and the start node are converted by the graph pass (spec 239), not by member rules.
        Catalog("Flowchart", Flowchart,
            [Slot("Activities", "Flowchart.Activities"), Structure("Variables"), Graph("Connections"), Graph("Start", Elsa3MemberKind.Port)]),
        Catalog("Break", Primitives + "Break", [], [], null, BreakStopsAtParallel),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> FlowModel() =>
    [
        Catalog("FlowDecision", If,
            [Carry("Condition", "Condition") with { OnNull = NullUnbound }],
            [Outcome("True", "True"), Outcome("False", "False")]),
        Catalog("FlowSwitch", Switch,
            [Structure("Cases"), Structure("Mode", Elsa3MemberKind.Input)],
            [Outcome("Default", "Default")],
            Elsa3NodeRewriteKind.SwitchValueSynthesis),
        // Elsa 4 needs no node for these (spec 239 G-2): fan-out connections, an implicit join, the start node id.
        GraphConstruct("FlowFork", Elsa3GraphConstructKind.Fork, Structure("Branches", Elsa3MemberKind.Input)),
        GraphConstruct("FlowJoin", Elsa3GraphConstructKind.Join, Structure("Mode", Elsa3MemberKind.Input)),
        GraphConstruct("Start", Elsa3GraphConstructKind.Start),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> PrimitivesAndIntrinsics() =>
    [
        Catalog("WriteLine", Primitives + "WriteLine", [DefaultFlip("Text", "text", "")]),
        Catalog("ReadLine", Primitives + "ReadLine", [CarryOutput("Result", "Result")]),
        Catalog("Fault", Primitives + "Fault",
            [
                DefaultFlip("Code", "code", "0"),
                DefaultFlip("Category", "category", "General"),
                DefaultFlip("FaultType", "faultType", "System"),
                Carry("Message", "message") with { OnNull = NullAsNull }
            ]),
        Intrinsic("SetVariable", Elsa3IntrinsicContracts.Set,
            [Structure("Variable", Elsa3MemberKind.Input), Carry("Value", Elsa3IntrinsicContracts.ValueKey) with
                {
                    OnNull = NullRefused("Elsa 3 sets the variable to null, which Elsa 4 cannot publish as a value (#3280).")
                }
            ],
            Elsa3NodeRewriteKind.VariableReference),
        Intrinsic("SetOutput", Elsa3IntrinsicContracts.SetOutput,
            [
                Narrow("OutputName", Elsa3IntrinsicContracts.NameKey, Elsa3ValueContract.LiteralOnly) with { OnNull = NullRequired },
                Carry("OutputValue", Elsa3IntrinsicContracts.ValueKey) with
                {
                    OnNull = NullRefused("Elsa 3 sets the workflow output to null, which Elsa 4 cannot publish as a value (#3280).")
                }
            ],
            Elsa3NodeRewriteKind.ReusableOutputWrite),
        Intrinsic("Correlate", Elsa3IntrinsicContracts.SetCorrelationId,
            [Carry("CorrelationId", Elsa3IntrinsicContracts.ValueKey) with { OnNull = NullEmits("") }]),
        Intrinsic("SetName", Elsa3IntrinsicContracts.SetInstanceName,
            [Carry("Value", Elsa3IntrinsicContracts.ValueKey) with { OnNull = NullEmits("") }]),
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
                Narrow("Path", "Path", Elsa3ValueContract.LiteralOnly) with { OnNull = NullRefused("Elsa 3 cannot route an endpoint without a path.") },
                Carry("SupportedMethods", "SupportedMethods") with { OnNull = NullAsNull },
                Carry("Authorize", "Authorize") with { OnNull = NullUnbound },
                Carry("Policy", "Policy") with { OnNull = NullAsNull },
                Carry("RequestTimeout", "RequestTimeout") with { OnNull = NullAsNull },
                Carry("RequestSizeLimit", "RequestSizeLimit", RequestSizeNoOutcome) with { OnNull = NullAsNull },
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
                Structure("QueryStringData", Elsa3MemberKind.Output),
                Structure("Headers", Elsa3MemberKind.Output),
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
            ],
            Elsa3NodeRewriteKind.HttpTriggerResponseMode),
        SendHttpRequest("SendHttpRequest", flow: false),
        SendHttpRequest("FlowSendHttpRequest", flow: true),
        Catalog("WriteHttpResponse", Http + "WriteHttpResponse",
            [
                Narrow("StatusCode", "StatusCode", Elsa3ValueContract.EnumNameToInt) with { OnNull = NullUnbound },
                Narrow("Content", "Body", Elsa3ValueContract.HttpResponseBody) with { BehaviourWarning = HttpResponseJsonVariant, OnNull = NullAsNull },
                Narrow("ContentType", "ContentType", Elsa3ValueContract.HttpResponseContentType) with { OnNull = NullAsNull },
                Carry("ResponseHeaders", "Headers") with { OnNull = NullRefused("Elsa 3 faults on null response headers.") }
            ]),
        NoCounterpart("DownloadHttpFile", "Elsa 4 has no file download activity."),
        NoCounterpart("WriteFileHttpResponse", "Elsa 4 has no file response activity."),
    ];

    private static Elsa3ActivityMapEntry SendHttpRequest(string elsa3Type, bool flow)
    {
        var members = new List<Elsa3MemberRule>
        {
            Carry("Url", "Url") with { OnNull = NullRequired },
            Narrow("Method", "Method", Elsa3ValueContract.HttpMethodSupported) with { OnNull = NullUnbound },
            Narrow("Content", "Content", Elsa3ValueContract.StringOnly) with { OnNull = NullAsNull },
            DefaultFlip("ContentType", "ContentType", "application/json", whenBound: "Content") with
            {
                OnNull = NullRefused("Elsa 3 sends no body when the content type is null.", whenBound: "Content")
            },
            Removed("Authorization", warning: HttpAuthorizationUseConnection),
            Removed("DisableAuthorizationHeaderValidation", "true", warning: AuthHeaderUnvalidated),
            Narrow("RequestHeaders", "RequestHeaders", Elsa3ValueContract.SingleHeaderValue) with { OnNull = NullRefused("Elsa 3 faults on null request headers.") },
            Removed("EnableResiliency", "false"),
            CarryOutput("StatusCode", "StatusCode"),
            SemanticsChanged("ParsedContent"),
            CarryOutput("ResponseHeaders", "ResponseHeaders"),
            Lost("Result")
        };
        if (flow)
        {
            members.Add(Carry("ExpectedStatusCodes", "ExpectedStatusCodes") with { OnNull = NullEmits("[200]") });
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
            [
                Narrow("CronExpression", "Expression", Elsa3ValueContract.LiteralOnly) with
                {
                    OnNull = NullRefused("Elsa 3 runs a Cron without an expression as a disabled trigger, which Elsa 4 has no counterpart for.")
                },
                StartOnly()
            ]),
        NoCounterpart("StartAt", "Elsa 4 has no start-at trigger."),
    ];

    private static IEnumerable<Elsa3ActivityMapEntry> RuntimeActivities() =>
    [
        Catalog("Event", Primitives + "Event",
            [
                Carry("EventName", "EventName") with { OnNull = NullRequired },
                // Elsa 3's Result is the event payload; Elsa 4's Result is the event name, its payload output Payload (#3107).
                CarryOutput("Result", "Payload"),
                new Elsa3MemberRule("CanStartWorkflow", Elsa3MemberKind.CustomProperty, Elsa3MemberRuleKind.DefaultFlip, "CanStartWorkflow", Literal("false"))
            ]),
        Catalog("PublishEvent", Primitives + "PublishEvent",
            [
                Carry("EventName", "EventName") with { OnNull = NullRequired },
                Carry("CorrelationId", "CorrelationId") with { OnNull = NullAsNull },
                Carry("IsLocalEvent", "IsLocalEvent") with { OnNull = NullUnbound },
                Narrow("Payload", "Payload", Elsa3ValueContract.JsonOnly) with { OnNull = NullAsNull }
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
            Narrow("WorkflowDefinitionId", "WorkflowDefinitionId", Elsa3ValueContract.LiteralOnly) with { OnNull = NullRequired },
            Carry("CorrelationId", "CorrelationId") with { OnNull = NullAsNull },
            Narrow("Input", "Inputs", Elsa3ValueContract.JsonOnly) with { OnNull = NullAsNull },
            Lost("Result"),
            Synthetic("CancelChildOnParentCancellation", "false")
        };
        if (executeWorkflow)
        {
            members.Add(DefaultFlip("WaitForCompletion", "WaitForCompletion", "true"));
        }
        else
        {
            members.Add(Carry("WaitForCompletion", "WaitForCompletion") with { OnNull = NullUnbound });
            members.Add(Removed("StartNewTrace", "false"));
            members.Add(Removed("ChannelName"));
        }

        return members.ToArray();
    }

    private static IEnumerable<Elsa3ActivityMapEntry> JavaScript() =>
    [
        Catalog("RunJavaScript", RunJavaScript,
            [
                Narrow("Script", "script", Elsa3ValueContract.ScriptBody) with { OnNull = NullRefused("Elsa 3 runs nothing without a script, and Elsa 4 requires one.") },
                Carry("PossibleOutcomes", "possibleOutcomes", ScriptOutcomeRouting) with { OnNull = NullAsNull },
                CarryOutput("Result", "result")
            ],
            // Elsa 3 completes with Done unless the script calls setOutcome (refused, #2717); Elsa 4 completes with
            // Unmatched when the return value names no possible outcome (spec 239 R-3).
            [Outcome("Done", "Unmatched")]),
    ];
}
