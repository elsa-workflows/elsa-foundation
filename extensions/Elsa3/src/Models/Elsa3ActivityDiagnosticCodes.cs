namespace Elsa3.Models;

/// <summary>
/// Stable codes the Elsa 3 activity conversion reports. <c>ELS3-ACTIVITY-*</c> codes are errors: the item is not
/// converted. <c>ELS3-BEHAVIOUR-*</c> codes are warnings: the item converts, but Elsa 4 behaves differently at run time
/// in a way the JSON cannot reveal. A code is never renamed or reused; spec 235 skips and reports items by code.
/// </summary>
public static class Elsa3ActivityDiagnosticCodes
{
    /// <summary>A literal has multiple possible exact target names or would overwrite a member after rewriting.</summary>
    public const string LiteralKeyAmbiguous = "ELS3-LITERAL-KEY-AMBIGUOUS";
    /// <summary>A literal key was rewritten to the target's exact JSON name.</summary>
    public const string LiteralKeyRewritten = "ELS3-LITERAL-KEY-REWRITTEN";
    /// <summary>A legacy date literal cannot establish an instant without a source interpretation or offset.</summary>
    public const string LiteralDateOffsetMissing = "ELS3-LITERAL-DATE-OFFSET-MISSING";
    /// <summary>A legacy date literal was given the configured source interpretation.</summary>
    public const string LiteralDateInterpreted = "ELS3-LITERAL-DATE-INTERPRETED";
    /// <summary>A converted date may show a different clock or date from the Elsa 3 host.</summary>
    public const string LiteralDateWallClock = "ELS3-LITERAL-DATE-WALL-CLOCK";

    /// <summary>The node's type is not in the built-in map, not claimed by a replaced-type alias and not a catalog key.</summary>
    public const string UnmappedType = "ELS3-ACTIVITY-UNMAPPED-TYPE";

    /// <summary>Two activity definitions' highest versions claim to replace the same Elsa 3 type.</summary>
    public const string AmbiguousAlias = "ELS3-ACTIVITY-AMBIGUOUS-ALIAS";

    /// <summary>The activity, or this use of it, has no Elsa 4 counterpart.</summary>
    public const string NoCounterpart = "ELS3-ACTIVITY-NO-COUNTERPART";

    /// <summary>The target type key has no valid-SemVer version in the catalog.</summary>
    public const string VersionUnavailable = "ELS3-ACTIVITY-VERSION-UNAVAILABLE";

    /// <summary>The target activity version is in the catalog, but its structure facet cannot be read.</summary>
    public const string StructureUnreadable = "ELS3-ACTIVITY-STRUCTURE-UNREADABLE";

    /// <summary>A property is neither a mapped Elsa 3 member nor a declared Elsa 4 member.</summary>
    public const string UnknownMember = "ELS3-ACTIVITY-UNKNOWN-MEMBER";

    /// <summary>A captured member whose Elsa 4 counterpart has different semantics.</summary>
    public const string MemberSemanticsChanged = "ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED";

    /// <summary>A value does not provably fit the narrower Elsa 4 contract.</summary>
    public const string ValueOutsideContract = "ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT";

    /// <summary>A variable's starting value cannot be converted to the variable's own type by Elsa 3's rule.</summary>
    public const string VariableValueUnconvertible = "ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE";

    /// <summary>
    /// A member is null, and what Elsa 3 ran it with cannot be reproduced or represented in Elsa 4 (spec 267): Elsa 3
    /// hung, faulted, dropped data or rejected the activity, or Elsa 4 cannot publish the null.
    /// </summary>
    public const string NullUnconvertible = "ELS3-ACTIVITY-NULL-UNCONVERTIBLE";

    /// <summary>A captured output has no Elsa 4 counterpart and no exact projection.</summary>
    public const string OutputNotRepresentable = "ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE";

    /// <summary>An input with no Elsa 4 counterpart is bound to a non-default value or an expression.</summary>
    public const string RemovedInputBound = "ELS3-ACTIVITY-REMOVED-INPUT-BOUND";

    /// <summary>A required Elsa 4 input would stay unbound.</summary>
    public const string RequiredInputUnbound = "ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND";

    /// <summary>
    /// A mode or placement Elsa 4 cannot express: a mode set by an expression, a body-containing MatchAny Switch with incompatible control labels/outcomes, a misplaced Complete/End, an outcome set by an expression, several outcomes outside a reusable workflow, ….
    /// </summary>
    public const string UnsupportedMode = "ELS3-ACTIVITY-UNSUPPORTED-MODE";

    /// <summary>
    /// Retired 2026-10-19 (spec 247): never raised. Graph conversion (spec 239) converts Flowchart, FlowFork, FlowJoin and
    /// Start, and the map target that raised it is removed. Kept because a code is never removed or reused.
    /// </summary>
    public const string GraphConversionPending = "ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING";

    /// <summary>Two activities of one flowchart share an id, so connections and Elsa 4 node ids cannot tell them apart.</summary>
    public const string GraphDuplicateNodeId = "ELS3-ACTIVITY-GRAPH-DUPLICATE-NODE-ID";

    /// <summary>A connection names a node that is not a child of its flowchart, or a container that is not a flowchart has connections.</summary>
    public const string GraphConnectionInvalid = "ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID";

    /// <summary>A connection's port is not mapped, and the Elsa 4 activity does not emit it.</summary>
    public const string GraphOutcomeUnmapped = "ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED";

    /// <summary>A FlowJoin Elsa 4 cannot express exactly: WaitAny with concurrent inbound paths, WaitAll with a path that may not run, or a mode set by expression.</summary>
    public const string GraphJoinUnsupported = "ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED";

    /// <summary>A loop (back connection) whose shape Elsa 4's flowchart rejects.</summary>
    public const string GraphLoopUnsupported = "ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED";

    /// <summary>A Start activity or a FlowFork at the start leads to more than one node; an Elsa 4 flowchart starts at one.</summary>
    public const string GraphStartAmbiguous = "ELS3-ACTIVITY-GRAPH-START-AMBIGUOUS";

    /// <summary>
    /// A start trigger sits where an Elsa 4 run cannot begin: a flowchart child with an inbound connection that is not the
    /// start node (spec 238's start-path rule, which refuses it at publish).
    /// </summary>
    public const string GraphStartTriggerMisplaced = "ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED";

    /// <summary>
    /// A reusable workflow's body uses an activity that converts to an Elsa 4 engine intrinsic (SetVariable, SetOutput,
    /// Correlate, SetName). A graph activity resolves every node to a published activity version and intrinsics have
    /// none, so the reusable cannot be published (spec 246 D-7).
    /// </summary>
    public const string ReusableIntrinsicUnsupported = "ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED";

    /// <summary>Activities nest deeper than the conversion supports.</summary>
    public const string NestingTooDeep = "ELS3-ACTIVITY-NESTING-TOO-DEEP";

    /// <summary>Two properties bind one input, or one property matches two inputs.</summary>
    public const string DuplicateInputBinding = "ELS3-ACTIVITY-DUPLICATE-INPUT-BINDING";

    /// <summary>
    /// A literal DispatchWorkflow/ExecuteWorkflow input map passes a name the target workflow does not declare: Elsa 3
    /// ignored it, Elsa 4 refuses it (#5808).
    /// </summary>
    public const string DispatchInputUndeclared = "ELS3-ACTIVITY-DISPATCH-INPUT-UNDECLARED";

    /// <summary>
    /// A BulkDispatchWorkflows target is neither in the same import nor already published in Elsa 4, so the converter
    /// cannot build the per-item inputs from its declared inputs (spec 1242 FR-016).
    /// </summary>
    public const string BulkDispatchTargetUnknown = "ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN";

    /// <summary>
    /// A BulkDispatchWorkflows <c>Completed</c> or <c>Canceled</c> connection has no faithful Elsa 4 meaning: the Elsa 4
    /// activity completes with <c>Done</c> only (spec 1242 FR-017).
    /// </summary>
    public const string BulkDispatchOutcomeRoute = "ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE";

    /// <summary>
    /// A BulkDispatchWorkflows branch reads what Elsa 3 passed it about the child (the <c>ChildInstanceId</c> variable, or
    /// the input <c>WorkflowOutput</c>, <c>WorkflowInstanceId</c>, <c>WorkflowStatus</c> or <c>WorkflowSubStatus</c>),
    /// which an Elsa 4 branch does not get (spec 1242 FR-019).
    /// </summary>
    public const string BulkDispatchChildResultRead = "ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ";

    /// <summary>HttpEndpoint.ParsedContent: Elsa 4 does not parse form posts, and text becomes a string.</summary>
    public const string FormPostNotParsed = "ELS3-BEHAVIOUR-FORM-POST-NOT-PARSED";

    /// <summary>HttpEndpoint.RequestSizeLimit: an oversized request has no outcome in Elsa 4.</summary>
    public const string RequestSizeNoOutcome = "ELS3-BEHAVIOUR-REQUEST-SIZE-NO-OUTCOME";

    /// <summary>SendHttpRequest outcomes: Done no longer co-fires, and non-2xx goes to Failed.</summary>
    public const string HttpOutcomeSingle = "ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE";

    /// <summary>
    /// DispatchWorkflow/ExecuteWorkflow input map bound to an expression or a captured value: Elsa 3 ignored names the
    /// target did not declare and matched names case-insensitively; Elsa 4 refuses both at run time (#5808).
    /// </summary>
    public const string DispatchInputNames = "ELS3-BEHAVIOUR-DISPATCH-INPUT-NAMES";

    /// <summary>SendHttpRequest: Elsa 4 does not validate the Authorization header.</summary>
    public const string AuthHeaderUnvalidated = "ELS3-BEHAVIOUR-AUTH-HEADER-UNVALIDATED";

    /// <summary>SendHttpRequest.Authorization was removed; bind an http connection instead.</summary>
    public const string HttpAuthorizationUseConnection = "ELS3-BEHAVIOUR-HTTP-AUTHORIZATION-CONNECTION";

    /// <summary>While.Condition is re-read only from persisted state.</summary>
    public const string WhilePersistedCondition = "ELS3-BEHAVIOUR-WHILE-PERSISTED-CONDITION";

    /// <summary>For.Step 0 faults in Elsa 4.</summary>
    public const string ForZeroStepFaults = "ELS3-BEHAVIOUR-FOR-ZERO-STEP-FAULTS";

    /// <summary>Break does not cross Parallel or Fork branches in Elsa 4.</summary>
    public const string BreakStopsAtParallel = "ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL";

    /// <summary>Fork WaitAny: cancellation of the losing branches is not verified to match.</summary>
    public const string ForkWaitAnyCancellation = "ELS3-BEHAVIOUR-FORK-WAIT-ANY-CANCELLATION";

    /// <summary>ExecuteWorkflow becomes a durable dispatch instead of an in-process call.</summary>
    public const string ExecuteWorkflowDurable = "ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE";

    /// <summary>
    /// A null member of an alias or a verbatim catalog key gets the Elsa 4 input's default; Elsa 3 ran it with its own
    /// activity's default, which the conversion cannot see (#3293).
    /// </summary>
    public const string NullTakesElsa4Default = "ELS3-BEHAVIOUR-NULL-TAKES-ELSA4-DEFAULT";

    /// <summary>A reusable workflow's Complete names an outcome the definition does not declare; it is declared anyway.</summary>
    public const string OutcomeUndeclared = "ELS3-BEHAVIOUR-OUTCOME-UNDECLARED";

    /// <summary>A reusable workflow's declared outcome is reached by no Complete; it is dropped from the contract.</summary>
    public const string OutcomeUnreached = "ELS3-BEHAVIOUR-OUTCOME-UNREACHED";

    /// <summary>
    /// WriteHttpResponse: an object body with a JSON content type Elsa 3 did not match exactly (letter case, a parameter,
    /// a <c>+json</c> type). Elsa 3 sent the value's type name; Elsa 4 sends the JSON.
    /// </summary>
    public const string HttpResponseJsonVariant = "ELS3-BEHAVIOUR-HTTP-RESPONSE-JSON-VARIANT";

    /// <summary>A connection Elsa 3 never follows (leaving Complete, End or Break, or a FlowFork port outside its branches) is removed.</summary>
    public const string DeadConnectionRemoved = "ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED";

    /// <summary>
    /// BulkDispatchWorkflows: only declared inputs are passed, a child-declared <c>ParentInstanceId</c> is no longer filled,
    /// <c>ChildFaulted</c> also runs for cancelled and undispatchable children, a child without a correlation function
    /// inherits the parent's correlation id, and an expression wait is evaluated once per pass (spec 1242 FR-016).
    /// </summary>
    public const string BulkDispatchItemInputs = "ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS";

    /// <summary>
    /// BulkDispatchWorkflows: more than 1,000 items fault in Elsa 4, and at most 16 passes are open at once, where Elsa 3
    /// dispatched every child at once (spec 1242 FR-020).
    /// </summary>
    public const string BulkDispatchCapped = "ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED";

    /// <summary>
    /// BulkDispatchWorkflows: a converted branch writes a variable declared outside the bulk node. Elsa 3 ran the branches
    /// one at a time; Elsa 4 runs them concurrently, so a shared value can lose updates (spec 1242 FR-020a).
    /// </summary>
    public const string BulkDispatchOuterWrite = "ELS3-BEHAVIOUR-BULK-DISPATCH-OUTER-WRITE";
}
