# Contract: JavaScript precision policy

- **Kind**: replacement contract. Exactly one implementation is active per host.
- **Default**: `RefusingPrecisionPolicy`, installed by the JavaScript expression feature. It has no options.
- **Replace**: a host feature registers its own implementation in place of the default. This is the only way to
  handle a loss differently (D-17).
- **Scope**: host-wide. No per-workflow or per-node override.

## Called when

A change or a result (of a script, or of an input expression into an input) is about to be written and it carries at
least one lossy path.

## Input

- The target being written: `result`, or the value key such as `variables.order`. The node and the input are not
  passed: a refusal faults the step, or blocks it for an input, and the fault or incident names them.
- Each lossy path: stored text, written text.
- Whether the write is a whole-array write.

## Output

- Allow or refuse.
- Diagnostics: each with path, stored, written, reason, remedy and severity.

## Default behaviour

Always refuse, with the error diagnostics of the JavaScript adapter contract. Nothing of the step is written.

Computing with a lossy value and passing one to a host function never reach the policy: they warn.
