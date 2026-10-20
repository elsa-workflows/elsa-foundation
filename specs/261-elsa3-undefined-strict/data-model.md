# Data model: spec 261

No persisted data or model type changes. The unit changes the text of `ExpressionDefinition.Source` that the converter
emits for JavaScript, and adds refusals (`Elsa3ExpressionRewriteIssue`, code `VF-IMP-007`) with the messages in
[contracts/javascript-strict-and-undefined.md](contracts/javascript-strict-and-undefined.md).

- **Converted expression** (`ExpressionDefinition`, unchanged shape): `Source` gains `(…) ?? null` in Expression mode
  and, in Script mode, a trailing `\n` after a last-line comment. `Parameters`, `ResultType`, `Options` and the
  `binding-pure-v1` capability profile are unchanged.
- **Conversion diagnostic** (`Elsa3ExpressionRewriteIssue`, unchanged shape): five new message/guidance pairs under the
  existing code `VF-IMP-007`.
