# Contract: diagnostic codes (spec 236)

Public constants in `Elsa3.Models.Elsa3ActivityDiagnosticCodes`. The string values are a stable contract: spec 235
skips and reports by code, so a code is never renamed or reused. Each diagnostic carries:

- definition id and version;
- node id;
- JSON path;
- property (where relevant);
- Elsa 3 type;
- guidance (`Elsa3MigrationDiagnostic`).

## Errors: the item is unconvertible

| Constant | Code | Raised when | FR |
|---|---|---|---|
| `UnmappedType` | `ELS3-ACTIVITY-UNMAPPED-TYPE` | not in the map, not claimed by `ReplacesActivityType`, not a catalog key | FR-007 |
| `AmbiguousAlias` | `ELS3-ACTIVITY-AMBIGUOUS-ALIAS` | two definitions' highest versions claim the same Elsa 3 type | FR-009 |
| `NoCounterpart` | `ELS3-ACTIVITY-NO-COUNTERPART` | R6 activity (ParallelForEach, BulkDispatchWorkflows, …) or R6 use (mid-flow Timer/Cron, a SendHttpRequest port holding an activity) | FR-003 |
| `VersionUnavailable` | `ELS3-ACTIVITY-VERSION-UNAVAILABLE` | the target key has no valid-SemVer version in the catalog | FR-008 |
| `StructureUnreadable` | `ELS3-ACTIVITY-STRUCTURE-UNREADABLE` | the target version is in the catalog, but its structure facet cannot be read (added in review) | FR-008 |
| `UnknownMember` | `ELS3-ACTIVITY-UNKNOWN-MEMBER` | a property neither in the map nor a declared Elsa 4 member | FR-010 |
| `MemberSemanticsChanged` | `ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED` | captured `SendHttpRequest.ParsedContent` | FR-004 |
| `ValueOutsideContract` | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` | R5 value does not provably fit (non-literal where literal-only, non-string, non-JSON, literal credential) | FR-004 |
| `OutputNotRepresentable` | `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE` | R4 output captured and no exact projection exists | FR-004 |
| `RemovedInputBound` | `ELS3-ACTIVITY-REMOVED-INPUT-BOUND` | R3 input absent in Elsa 4 but bound to a non-default value or an expression | FR-004 |
| `RequiredInputUnbound` | `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND` | a required Elsa 4 input would stay unbound | FR-004c |
| `UnsupportedMode` | `ELS3-ACTIVITY-UNSUPPORTED-MODE` | Switch/FlowSwitch MatchAny or expression mode; End/Complete in a plain workflow outside the root flowchart; Complete with several outcomes or an expression outcome; `"done"` alongside `"Done"` in a reusable workflow | FR-004a, FR-004e |
| `GraphConversionPending` | `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` | Flowchart, FlowFork, FlowJoin, Start: graph conversion is #2705's | FR-001, FR-006 |
| `NestingTooDeep` | `ELS3-ACTIVITY-NESTING-TOO-DEEP` | deeper than `MaxNestingDepth` (formerly `ArgumentException`) | FR-012 |
| `DuplicateInputBinding` | `ELS3-ACTIVITY-DUPLICATE-INPUT-BINDING` | two properties bind one input, or one property matches two (formerly `ArgumentException`) | FR-012 |

## Warnings: the item converts (W)

| Constant | Code | Activity / member |
|---|---|---|
| `FormPostNotParsed` | `ELS3-BEHAVIOUR-FORM-POST-NOT-PARSED` | HttpEndpoint.ParsedContent |
| `RequestSizeNoOutcome` | `ELS3-BEHAVIOUR-REQUEST-SIZE-NO-OUTCOME` | HttpEndpoint.RequestSizeLimit |
| `HttpOutcomeSingle` | `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE` | SendHttpRequest outcomes (Done no longer co-fires; non-2xx → Failed) |
| `AuthHeaderUnvalidated` | `ELS3-BEHAVIOUR-AUTH-HEADER-UNVALIDATED` | SendHttpRequest.DisableAuthorizationHeaderValidation absent or false |
| `WhilePersistedCondition` | `ELS3-BEHAVIOUR-WHILE-PERSISTED-CONDITION` | While.Condition |
| `ForZeroStepFaults` | `ELS3-BEHAVIOUR-FOR-ZERO-STEP-FAULTS` | For.Step |
| `BreakStopsAtParallel` | `ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL` | Break inside Parallel/Fork branches |
| `ForkWaitAnyCancellation` | `ELS3-BEHAVIOUR-FORK-WAIT-ANY-CANCELLATION` | Fork.JoinMode WaitAny |
| `ExecuteWorkflowDurable` | `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` | ExecuteWorkflow |
| `ScriptOutcomeRouting` | `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING` | RunJavaScript.PossibleOutcomes |
| `OutcomeUndeclared` | `ELS3-BEHAVIOUR-OUTCOME-UNDECLARED` | Complete in a reusable workflow names an outcome the definition's `Outcomes` does not declare; it is declared in the contract |
| `OutcomeUnreached` | `ELS3-BEHAVIOUR-OUTCOME-UNREACHED` | a reusable workflow's declared outcome (other than `Done`) that no Complete reaches; dropped from the contract, so callers get no port for it |

`BoundaryOutcomeUnsupported` (`ELS3-ACTIVITY-BOUNDARY-OUTCOME-UNSUPPORTED`) was reserved while #2750 was open. It was
never released, so it is removed rather than retired (plan D-10).

The coverage test asserts that every `BehaviourWarning` the map declares is one of these constants, and that every
constant is used by at least one map entry.
