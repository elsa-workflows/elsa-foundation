# Contract: JavaScript precision policy

- **Kind**: replacement contract. Exactly one implementation is active per host.
- **Default**: `RefusingPrecisionPolicy`, installed by the JavaScript expression feature. It has no options.
- **Replace**: a host feature registers its own implementation in place of the default.
- **Scope**: host-wide. No per-workflow override.

## Called when

A detected change or a script result is about to be written and it carries at least one lossy path.

## Input

- The node and the value key being written.
- Each lossy path: stored text, written text.
- Whether the write is a whole-array write.
- The node's `AcceptPrecisionLoss` setting.

## Output

- Allow or refuse.
- Diagnostics: each with path, stored, written, reason, remedy and severity.

## Default behaviour

| Situation | Outcome |
|---|---|
| Node does not accept loss | Refuse; error diagnostics as in the JavaScript adapter contract; nothing of the step is written |
| Node accepts loss | Allow; a warning diagnostic per lossy path |

Computing with a lossy value and passing one to a host function never reach the policy: they warn.
