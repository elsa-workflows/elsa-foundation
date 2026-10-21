# Research: Elsa 3 converter: workflow variables that can be published

## R-1: Why publishing fails, and what "no starting value" means in Elsa 4

**Finding**:
- `Elsa3VariableMapper.Map` returns `new VariableDefinition(…, Default: new ArgumentValue(source.Value))`. The
  expression type is `null`, and the `ArgumentValue` is never `null`, even when `source.Value` is.
- `ExecutableNodeCompiler` compiles every non-null `Default` through `RuntimeInputBindingCompiler.Compile`
  (`ExecutableNodeCompiler.cs`, the variable-declaration builder).
- `Compile` matches the expression type against `Literal`, `Object`, `Variable`, `WorkflowRequest`, `ActivityResult`,
  `Default`, secrets and connections. With `null` it falls through to `CompileExpressionInput`, which throws
  `Activity node '{nodeId}' input '{key}' does not declare an expression type.` The node is the workflow root, `workflow`.
- Elsa 4's own authoring never produces this shape: `WorkflowBuilder.Variable` marks the initial value
  `AuthoringExpressionTypes.Literal`, and `BpmnDocumentImporter` passes `Default: null`.
- With no initial binding, `RuntimeVariableDeclarationProjector` starts the variable as an absent value
  (`ValueEnvelope.Absent`). That is how a natively authored variable with no default starts.

**Decision**: `Default: null` when Elsa 3 has no value (D-1). A `Literal` otherwise (D-3).

**Alternatives considered**:
- Fix publication to treat an untyped default as a literal. Rejected: publication is correct; the converter emits a
  shape no Elsa 4 author produces (boundary: converter only).
- `Literal` holding `null` for "no value", which starts the variable as a present `null`. Rejected: Elsa 4's own
  authoring uses no default, and reading the variable gives `null` either way.

## R-2: Elsa 3's rule for a starting value, and Elsa 4's copy of it

**Finding** (elsa-core `release/3.6.3` and tag `3.5.3`, `VariableMapper.Map(VariableModel)`; the JTest corpus's tool
version is 3.5):
- The value is text (`VariableModel.Value` is a `string?`).
- `source.Value.TryConvertTo(type)`: on success the variable gets the converted value; on failure a warning is logged
  and the variable keeps `null`.
- `ObjectConverter.ConvertTo` (elsa-core `Elsa.Expressions/Helpers/ObjectConverter.cs`): `null` → `null`; target
  assignable from `string` (`object`, `string`) → the text; a non-primitive, non-`object` target with text starting
  `{` or `[` → JSON-deserialised; `byte[]` → base64; otherwise the remaining branches (enums, `IConvertible`, type
  converters, …).
- Elsa 4's `ObjectConverter` (`Essentials/src/Serialization/SystemText/Services/ObjectConverter.cs`, behind
  `IObjectConverter`) carries the same branches: the string-to-structured rule is at the same lines (151, 165, 174).

**Decision**: Apply Elsa 3's rule by calling `IObjectConverter.TryConvertTo(source.Value, clrType)` in the converter
(D-2). `clrType` is the type `LegacyClrTypeResolver` already resolves, made an array type when `isArray` is set.

**Alternatives considered**:
- Hand-write the rule. Rejected: the full rule is hundreds of lines, and Elsa 4 already keeps it.
- Let publication's `ConvertLiteral` convert the raw text. Rejected: it is a different rule. A `List<String>` value
  `["a","b"]` would be split on commas into `["[\"a\"", "\"b\"]"]`, and a dictionary from JSON text would fail with
  `VF-COER-001` at publish.

## R-3: How the converted value reaches publication unchanged

**Finding**: `RuntimeInputBindingCompiler.ConvertLiteral` for a `JsonElement`:
- `Object`/`Array` → `Deserialize(targetType)`;
- `String` → the string, then converted to the target;
- number or boolean → its text, then `Convert.ChangeType` (or `Enum.Parse`).

So a value that has already been converted, written as JSON, comes back as the same value:
- an `Object` variable's text stays a JSON string, and so stays the text;
- `5` and `true` parse back;
- an enum serialises as its number, which `Enum.Parse` accepts;
- a list or dictionary is a JSON array or object, which deserialises into the same type.

**Decision**: Carry the converted value as `JsonSerializer.SerializeToElement(converted)` in a `Literal` (D-3).

**Alternatives considered**:
- The `Object` expression type for structured values. Rejected: it adds a second path for no gain once the value is
  already JSON.
- Keep the raw Elsa 3 text in the literal. Rejected (R-2).

## R-4: Where a refusal can be reported

**Finding**:
- `Elsa3DefinitionConverter.ConvertAsync` stops on `plan.HasErrors` after `PrepareCoreAsync` (normalise and lower),
  then calls `Elsa3WorkflowDefinitionToState.Map`, which is where workflow variables are mapped today. A failure there
  can only become `ELS3-MIGRATION-DEFINITION-MAPPING-FAILED`.
- Container variables are mapped in `Elsa3ActivityNormalizer.ReadContainerVariables`, which reports through
  `context.Error`.
- Rewrites add synthesized variables to `plan.Definition.Variables` (`Elsa3HttpTriggerResponseModeRewrite`,
  `Elsa3ReusableOutputWriteRewrite`). They have no value, so they never fail.

**Decision**: `Elsa3ActivityNormalizer.NormalizeAsync` checks the definition's own variables before visiting the tree
and adds a refusal at `$.variables[i]` with `Elsa3ConversionDiagnostics.ForDefinition`. `ReadContainerVariables` uses
the same check through `context.Error`. The state mapper maps workflow variables with the same mapper afterwards; it
only sees variables that passed, or synthesized ones (D-4).

**Alternatives considered**: report from the state mapper. Rejected: too late for a coded refusal.

## R-5: Diagnostic code

**Finding**: `Elsa3ActivityDiagnosticCodes` holds the conversion errors, including definition-level ones
(`ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`). `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` means a value that does not
fit a narrower Elsa 4 contract. Here the value does not fit its own declared type even in Elsa 3. Codes are never reused.

**Decision**: a new error code, `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` (D-5), with the message and guidance in
[contracts/variable-starting-values.md](contracts/variable-starting-values.md). A converted value that cannot be
serialised to JSON gets the same code, so nothing that converts can fail at publish.

## R-6: Dependency envelope

**Finding**: `Elsa3.Mapping` references `Elsa.Serialization.Core`, which defines both `IWellKnownTypeRegistry` and
`IObjectConverter`. The `Serialization` feature (`SerializationFeature`) registers both, and the mapping already needs
the registry from it. The Mapping tests build the registry through `SerializationServices.TypeRegistry()`, and
`SerializationServices.ObjectConverter()` exists beside it.

**Decision**: inject `IObjectConverter` into `Elsa3ActivityNormalizer` and `Elsa3WorkflowDefinitionToState`. No
project or feature dependency is added.

## R-7: Reusable activities

**Finding**: `Elsa3ReusableActivityImportMaterializer.BuildGraphManifest` writes each variable's `initialValue` as
`{ syntax: Default.ExpressionType ?? "Literal", value: Default.Value }`, or `null` when `Default` is `null`. Today,
every reusable variable gets `{ syntax: "Literal", value: <raw text or null> }`. That is why reusables publish while
regular workflows fail.

**Decision**: no change there. After D-1 and D-3, a reusable variable with no value gets `initialValue: null`, and one
with a value gets the converted `Literal`. The existing reusable tests cover the first case.

## R-8: JTest corpus

**Method** (D-7): a throwaway test, deleted afterwards, reads every definition in the corpus. It runs each workflow
variable and each container variable through `Elsa3VariableMapper` with the real registry and converter, and counts
variables with a value, how many convert and how many are refused. It also records whether the definitions that
convert end to end today still convert.

**Result**: recorded in the spec after implementation.
