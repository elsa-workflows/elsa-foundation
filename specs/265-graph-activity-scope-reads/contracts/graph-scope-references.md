# Contract: graph-scope references and publication refusals

## Meaning of a body reference (closed-scope graph activity)

| Authored reference in a body node | Resolves to | Refused when |
|---|---|---|
| `WorkflowRequest {memberKey: k}` (binding or JS parameter) | Graph input `k` (reference key): the caller-passed value, else its default, else `null` | `k` is not a declared graph input |
| `Input {referenceKey: k}` (Studio) | As `WorkflowRequest {memberKey: k}` | As above |
| `Variable {k, "workflow"}` / `{k, "activity-graph"}` | Graph variable `k` in this invocation | `k` is not a declared graph variable |
| `Variable {k, c}`, where `c` is a body container node id | Container `c`'s variable `k` in this placement | `c` is not an ancestor container of the node, or does not declare `k` |
| `ActivityResult {p, projection, scope?}` (binding or JS parameter) | Node `p`'s result in the same invocation | `p` is not a body node, or its contract has no such projection |
| Ambient JS `variables.k`, `getVariable('k')`, `getK()` | Graph or container variable `k` | Not checked statically. At run time it fails as for workflows. |
| Capture `outputs[] → Variable {k, "workflow"}` | Writes graph variable `k` | See the capture codes below |
| Any reference to a caller workflow value | — | Always: there is no such reference inside a body (D-1) |

## Refusal codes

All are raised by `GraphActivityProvider` at validation and compile, with `Severity = Error`. The pointer is the
body node's JSON pointer in the manifest. Each code below gives its message pattern.

| Code | Message pattern |
|---|---|
| `activity.graph.body-input-unknown` | Body node '{node}' input '{input}' reads graph input '{k}', which this activity does not declare. |
| `activity.graph.body-variable-unknown` | Body node '{node}' input '{input}' reads graph variable '{k}', which this activity does not declare. |
| `activity.graph.body-variable-scope-invisible` | Body node '{node}' input '{input}' reads variable '{k}' of scope '{c}', which is not a container around this node. |
| `activity.graph.body-producer-unknown` | Body node '{node}' input '{input}' reads the result '{projection}' of '{p}', which is not a node of this activity's body or has no such result. |
| `activity.graph.body-capture-target-unknown` | Body node '{node}' output '{output}' captures into '{k}' of scope '{s}'. A body can capture only into this activity's declared variables. |
| `activity.graph.body-capture-type-incompatible` | Body node '{node}' output '{output}' of type '{t1}' cannot be captured into graph variable '{k}' of type '{t2}' without a conversion. |
| `activity.graph.body-capture-concurrent` | Body nodes '{a}' and '{b}' can run at the same time and both capture into graph variable '{k}'. |

A workflow that uses an activity published under these rules never fails publication with `VF-COER-001` for a
body reference (SC-003).

## Compatibility

- Executables published before fingerprint `compiler/3.0.0` keep their behaviour. Their boundary node has no
  `ActivityExecutionScope`, so body nodes resolve as before.
- The next publication of such an activity applies these rules.

## Elsa 3

`ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED` is removed. Reads and memory-reference captures in a reusable body
convert to the references above. `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` stays until #3023.
