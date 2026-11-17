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

- The node and the value key being written.
- Each lossy path: stored text, written text.
- Whether the write is a whole-array write.

## Output

- Allow or refuse.
- Diagnostics: each with path, stored, written, reason, remedy and severity.

## Default behaviour

Always refuse, with the error diagnostics of the JavaScript adapter contract. Nothing of the step is written.

Computing with a lossy value and passing one to a host function never reach the policy: they warn.
