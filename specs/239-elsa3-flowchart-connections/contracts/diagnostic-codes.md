# Contract: diagnostic codes (spec 239)

New public constants in `Elsa3.Models.Elsa3ActivityDiagnosticCodes`, under spec 236's rules: a code is never renamed
or reused, and spec 235 skips and reports by code. Every diagnostic carries the fields of spec 236's contract.

## Errors: the item is unconvertible

| Constant | Code | Raised when | Guidance (gist) | FR |
|---|---|---|---|---|
| `GraphConnectionInvalid` | `ELS3-ACTIVITY-GRAPH-CONNECTION-INVALID` | a connection names a node that is not a child of its flowchart, or an id two children share; `connections` on a container that is not a flowchart | Remove or reconnect the connection in Elsa 3 | FR-012 |
| `GraphOutcomeUnmapped` | `ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED` | a connection's port is not mapped and the Elsa 4 activity does not emit it (including dynamic outcomes bound by expression) | Connect from an outcome the activity has, or make the outcome list literal | FR-003 |
| `GraphJoinUnsupported` | `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` | `WaitAny` join with concurrent inbound paths; `WaitAll` join with an inbound path that may not run; join mode by expression | Use `WaitAll`/`WaitAllActive`, remove the parallel branches, or make every branch unconditional | FR-006 |
| `GraphLoopUnsupported` | `ELS3-ACTIVITY-GRAPH-LOOP-UNSUPPORTED` | a back connection to anything but a start node with no other inbound connection; any back connection into a FlowFork or FlowJoin | Use a While, For or ForEach activity for the loop | FR-009 |
| `GraphStartTriggerMisplaced` | `ELS3-ACTIVITY-GRAPH-START-TRIGGER-MISPLACED` | a start-capable flowchart child that another activity leads into and that is not the start node (spec 238 D8 would refuse it at publish) | Move the trigger to an entry with no incoming connection, or make it a mid-flow wait | FR-013a |
| `GraphStartAmbiguous` | `ELS3-ACTIVITY-GRAPH-START-AMBIGUOUS` | a Start activity or a FlowFork at the start leads to more than one node | Put one activity first; an Elsa 4 flowchart starts at exactly one node | FR-008 |

Existing codes gain uses: `ELS3-ACTIVITY-NO-COUNTERPART` for a connection on a port the map marks unsupported;
`ELS3-ACTIVITY-UNSUPPORTED-MODE` for FlowFork branches by JavaScript, a graph construct outside a flowchart, and
`End` outside the root flowchart (amends spec 236 D-3).

## Warnings: the item converts (W)

| Constant | Code | Raised when |
|---|---|---|
| `DeadConnectionRemoved` | `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` | a connection Elsa 3 never follows is removed: it leaves `Complete`, `End` or `Break`, or a FlowFork port that is not one of its branches. One per connection |

## No longer raised

`GraphConversionPending` (`ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`) stays a constant, documented as no longer raised
by any built-in entry. It is not removed: spec 236 ships it, and codes are never reused.
