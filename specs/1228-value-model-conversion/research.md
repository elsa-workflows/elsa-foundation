# Research: Value model and conversion

All facts are from the initiative branch at `2c84e802f` unless stated.

## R-0 What the code does today

- **Values into scripts.** `IsolatedJintEngine.CreateValue` (lines 163–174) turns a JSON number into a `long` if it
  fits, else a `double`. Integers beyond 2^53 and long decimals lose digits for every value, read or not. Nothing
  reports it.
- **Frozen values.** `CreateObject` (176–188) and `CreateArray` (190–206) set non-writable descriptors and
  `PreventExtensions`. `ScriptValueIsolationTests.JavaScript_sees_frozen_plain_objects_with_no_clr_surface` asserts it.
- **Results out.** `JintResultMaterializer.Materialize` (32–73) runs the intrinsic `JSON.stringify` and parses the text
  back to a `JsonElement`. No CLR objects.
- **No writes.** No `setVariable`, no writable `variables`. Script writes do not exist today.
- **No CLR surface.** No `AllowClr` or proxy setting exists. D-16 holds already.
- **ForEach and snapshots** keep exact numbers since #3424 (`e0cc6433a`). Only the Jint part of #3421 is left.
- **Dynamic names.** `Object` maps to `typeof(object)` (`DefaultVariableTypeDescriptorProvider.cs:31`,
  `TypeAliasConvention.cs:37`). `Elsa.Any` is not registered anywhere; it is a string constant in about eight places
  (`IntrinsicAuthoringDescriptorProvider.cs:52`, `ValueRepresentation.cs:36`, `ValueConversionCompatibility.cs:61`,
  `ExecutableNodeCompiler.cs:274/475`, `ModifyVariableAlterationHandler.cs:91`, `ForEach.cs:165`,
  `RuntimeMemberRead.cs:27`, `RuntimeInputBindingStateProjection.cs:234`). Related checks:
  `RuntimeActivityInputMaterializer.cs:474/793`, `WorkflowIntrinsicExecutor.cs:783`, `GraphActivity.cs:368/375`,
  `PortableLiquidExpressionHandler.cs:80`, the Agent schema compiler.
- **No `DateOnly` or `TimeOnly` alias.** They fall back to their full .NET names.
- **#3167 is open.** `ValueRepresentationDefaults.Infer` strips only `System.`, so `System.Text.Json.JsonElement` and
  its nullable form infer `TypedValue`.
- **Dictionary types (#3202).** `TypeReferenceFactory.Decompose` (52–77) decomposes one level. `IDictionary<string,
  string[]>` becomes alias `System.String[]` with kind `Dictionary`. A dictionary of arrays cannot be described.
- **Elsa 3 untyped results (#3196).** `Elsa3ValueFlowLowerer.TypesMatch` (885–898) returns true when either side is
  untyped (`IsUntypedName`, 952–953). The import passes; publication refuses with `VF-COER-001`.
- **Precision.** The only 2^53 check is a design-time metadata check in `ClrAssemblyScanner.cs:37`. No policy seam.

## R-1 Change detection (Q2)

- **Decision**: copy, compare, patch. Keep the entered `JsonElement` per read value; compare after the run; write only
  changed paths; copy unchanged members from the entry.
- **Rationale**: it meets every V9 rule with no custom object per value, keeps real arrays, and makes FR-002 true by
  construction: a value with no change is never written.
- **Alternatives considered**: JavaScript proxies per value (needs a custom object per value; arrays are not real
  arrays; ruled out by the ADR); writing every read value back (breaks FR-002); `BigInt` for large integers
  (`JSON.stringify` throws on it, and mixing it with numbers throws).

## R-2 When a number is lossy (Q5)

- **Decision**: parse the JSON number text as a decimal (or as a big integer when it has no fraction or exponent),
  convert the double back to the shortest round-trip text, and compare values. Different value → lossy.
- **Rationale**: `10.50` and `10.5` are the same value; `9007199254740993` and `9007199254740992` are not. The check
  runs once per number at entry.
- **Alternatives considered**: a plain magnitude check (`|n| > 2^53 − 1`) misses long decimals such as
  `0.1000000000000000055511151231257827`; comparing texts flags `10.50` falsely.

## R-3 The acceptance surface (Q4)

- **Decision**: one node setting on script activities, `AcceptPrecisionLoss`, default off, compiled into the
  executable.
- **Rationale**: the ADR asks for an explicit acceptance recorded in the published contract. D-17 forbids options on
  the policy and a per-workflow override; a node choice is neither.
- **Alternatives considered**: no acceptance (needs an ADR wording change); a workflow-level switch (excluded by D-17).

## R-4 The write surface (Q3)

- **Decision**: script activities get a writable `variables`. Detected changes enter the step's variable session and
  commit with the step. Input-binding expressions keep a read-only `variables` (D-3).
- **Rationale**: only the change detector knows what changed. The ADR 0090 amendment makes script writes a general
  capability; ADR 0092 gives the base.
- **Dependency**: the S-10 session write path (#4316). Tasks for User Story 3 start after it lands on the branch.

## R-5 Elsa 3 coercion of an untyped result (Q7)

- **Decision**: record, per JSON kind and per typed target, what Elsa 3.6.3's `ObjectConverter.ConvertTo` produced.
  Convert where Elsa 4 gives the same value; refuse at import otherwise.
- **To do in implementation**: read the Elsa 3 source (release/3.6.3, checked against 3.5.3), as spec 267 did, and
  fill the table in the plan's task. Fix #3167 first and re-measure; it may make the JSON route work for more cases.

## R-6 One dynamic kind (Q1)

- **Decision**: register `Any` in `TypeAliasConvention`; resolve `Object`, `System.Object` and `Elsa.Any` to it; one
  `IsDynamicKind` helper replaces the string comparisons. Studio shows `Any` as "JSON".
- **Rationale**: one name for one kind. `object` as a variable type opens the door V10 keeps closed.
- **Dependency**: #3422 Q5.

## R-7 Nested types (Q6)

- **Decision**: `TypeReference` gets an optional `Element` reference, so a collection's element can be a collection.
- **Alternatives considered**: JSON Schema only (binding checks do not read it); a special alias for
  dictionary-of-arrays (does not generalise).
