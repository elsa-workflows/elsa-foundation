# Data model: unordered variable writes

All types are design-time. The structure contract is in `Elsa.Workflows.Design.Core`; the analyzer contract and its findings are in `Elsa.Workflows.Design.Validations.Core`. Nothing is persisted.

## ActivityChildTimeRelation (enum)

What a structure states about two of its direct children within one run of the structure.

| Value | Two different children | The same child paired with itself |
|---|---|---|
| `Unspecified` | The structure does not say. Treated as unordered (D-2b), and the finding says so. | Treated as "runs never overlap" (research R-5). |
| `FirstBeforeSecond` | Every run of the first ends before any run of the second starts. | Runs never overlap. |
| `SecondBeforeFirst` | The reverse. | Runs never overlap. |
| `Unordered` | Both can be in flight together; the definition does not fix their order. | Two runs can be in flight together. |
| `MutuallyExclusive` | At most one of them runs in one run of the structure. | (not used) |

## IActivityStructureHandler additions (contract)

| Member | Default | Meaning |
|---|---|---|
| `ActivityChildTimeRelation RelateChildren(ActivityNode activity, string firstChildNodeId, string secondChildNodeId)` | adapts `ChildrenCompletedBefore` (a child declared completed before another runs before it), otherwise `Unspecified` | The relation above. |
| `bool? CompletesAfterItsChildren(ActivityNode activity)` | `null` | `true`: the structure completes only after every child run it started has ended (or was cancelled before it could commit). `false`: it can complete while a child still runs. `null`: not declared, treated as `false`. |

`IActivityStructureService` gets the same two members, forwarding to the handler of the node's structure kind;
unknown kinds give `Unspecified` and `null`. Both have interface defaults so existing implementations keep compiling.

## VariableWrite

A declared write on one node: `(NodeId, Kind, ScopeId, ReferenceKey, OutputKey?)` where `Kind` is `Capture`, `Set`,
`Merge` or `Reduce`, and the target is normalized: blank, `workflow`, `$workflow` and the legacy `activity-graph`
scope are the root scope.

## UnorderedVariableWriteFinding

One per variable: `ScopeId`, `ReferenceKey`, the writer node ids in document order, the undeclared structures that
made a pair unordered (node id and structure kind), and the writer node ids that can run several times at once.

## DuplicateCaptureTargetFinding

One per node and variable when two of the node's outputs capture into the same variable: node id, output keys,
variable.

## Analysis rules

For two writes W1, W2 to one variable (different nodes), with C their nearest common ancestor and L, R the children
of C on their paths:

1. If an ancestor at or above C declares the child on the path self-`Unordered`, the pair is unordered.
2. Otherwise C's relation of L and R decides: `MutuallyExclusive` → no finding; `Unordered` or `Unspecified` →
   unordered; `FirstBeforeSecond` (L earlier) → unordered if L is self-`Unordered`, or if any structure from W1's
   parent up to and including L does not declare `CompletesAfterItsChildren == true`; otherwise ordered. The reverse
   for `SecondBeforeFirst`.
3. A single write is unordered with itself when an ancestor declares the child on its path self-`Unordered`.
4. Two different children with the same node id are never claimed ordered.
5. Two children that alternate around a cycle are declared with the same order for both argument orders; then both
   sides must be done (rule 2 applied to each side), since each runs before the other in turn.

A missing `CompletesAfterItsChildren` and a missing `RelateChildren` are named separately in the message.

The finding for a variable names every writer in an unordered pair or self-unordered.
