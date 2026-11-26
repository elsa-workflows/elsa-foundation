# Typed-Variable JavaScript Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Typing `variables.<name>.` in a Studio JavaScript expression lists the properties of the variable's declared CLR type, labeled as coming from the declared type.

**Architecture:** The CLR shape source starts describing an admitted declared type's members with a new advisory shape state, `Declared`. Validation and the Activity Data picker keep treating that state as unknown. The expression authoring context maps `Declared` members into the existing value-shape model with a documentation label, so the JavaScript provider's member completion and Studio's editor show them without any wire change.

**Tech Stack:** .NET (xUnit), System.Text.Json reflection-only shape reader, React + CodeMirror 6 (Vitest) in Studio.

**Spec:** [#5676](https://github.com/elsa-workflows/elsa-foundation/issues/5676) with the approved design in [comment 7139863519](https://github.com/elsa-workflows/elsa-foundation/issues/5676#issuecomment-7139863519), and [#5677](https://github.com/elsa-workflows/elsa-foundation/issues/5677). Owner decision (24 Nov 2026): trust the declared type, labeled.

## Global Constraints

- Label copy, exact: ``From the declared type `{alias}`; the stored value may differ.`` where `{alias}` is the variable's declared type alias.
- New enum member: `ActivitySerializedShapeState.Declared = 4`, appended last (existing ordinals unchanged). On the Workflows Design wire it serializes as `"declared"`.
- Reason code on a declared shape: `activity.shape.declared-type`. Every case that does not produce members keeps today's answer: `Unknown`, reason `activity.shape.serialization-profile-unbound`, `Type` set only when the member path is empty.
- Member names follow `ActivityValueJsonProfile` (camelCase), read through the existing `ClrSerializedShapeReader`.
- Only `CollectionKind.Single` declared types get members. Arrays, lists, dictionaries and other kinds keep today's answer.
- The declared type's CLR type comes from the design-time `IVariableTypeDescriptorCatalog` descriptor (`TypeDescriptor.ClrType`). Never consult the runtime alias registry (`IWellKnownTypeRegistry`).
- No change to the expression tooling wire contract (`ExpressionValueShape`, `ExpressionValueMember`, completion items). No new validation diagnostics anywhere.
- Scope is dotted paths only. Locals, indexing, callbacks, a TypeScript service, `get<Name>()` return shapes and write-path normalization are out.
- `main` takes pull requests only. Branch `agent/claude/5676-typed-variable-completion` for tasks 1–3 (one PR, closes #5676), then `agent/claude/5677-typed-variable-completion-studio` from the merged `main` for task 4 (closes #5677). Review with the `elsa-auto-review-merge` skill; Code Review merges.
- Run git with `-c core.fsmonitor=false` (fsmonitor hangs on this machine).
- Port 5095 belongs to the owner's own Workbench. Check ports with `lsof` before starting any server and never stop a process you did not start.
- PR and issue text: plain statements, no "not X, but Y" constructions. Posts through the owner's `gh` login start with `[Claude]`.

## Review Focus

1. **A declared type the reader cannot read** (a type with a custom `JsonConverter`, polymorphism or `JsonElement` members): the user expects no suggestions and no error. Task 1 pins it with a converter fixture returning today's Unknown answer.
2. **A recursive declared type** (`SerializedRecursive.Child`): the user expects a bounded list and a responsive editor. Task 1 pins it: the member count stays within the requested limits.
3. **A typed variable in the Activity Data picker**: the user expects the picker to look exactly as before. Task 2 pins it on the picker endpoint.
4. **A misspelled member after a typed variable** (`variables.customer.cty`): the user expects no red squiggle and no publish-time finding. Task 2 (analyzer) and task 3 (provider validation) pin it.
5. **A primitive-typed variable** (alias `String`): the user expects no invented members and no label. Task 1 pins `Declared` scalar with no members; task 3 pins that a member-less shape gets no label.

---

### Task 1: Describe a declared type's members as `Declared`

**Files:**
- Modify: `server/essentials/src/Activities/Design/Core/Models/ActivitySerializedShape.cs` (enum `ActivitySerializedShapeState`)
- Modify: `server/essentials/src/Activities/Design/Core/PublicAPI.Unshipped.txt`
- Modify: `server/essentials/src/Activities/Design/Core/Contracts/IActivitySerializedShapeSource.cs` (doc comment of `DescribeDeclaredTypeAsync`)
- Modify: `server/essentials/src/Activities/Design/Reconciliation/Clr/Services/ClrActivitySerializedShapeSource.cs` (`DescribeDeclaredTypeAsync`)
- Modify: `server/essentials/src/Activities/Design/Api/EXTENSION_POINTS.md` and `server/essentials/src/Activities/Design/Reconciliation/Clr/EXTENSION_POINTS.md` where they describe declared-type behavior (use the `elsa-extension-point-catalog` skill)
- Test: `server/essentials/tests/Activities/Design/Tests/Unit/ActivitySerializedShapeSourceTests.cs`

**Interfaces:**
- Produces: `ActivitySerializedShapeState.Declared` (value 4). `DescribeDeclaredTypeAsync` returns, for an admitted single-descriptor `CollectionKind.Single` type whose reader result is `Known` or `Truncated`, that reader result `with { State = Declared, ReasonCode = "activity.shape.declared-type" }`, `ProfileRevision == ActivityValueJsonProfile.Revision`, `SourceContractRevision == ownerContractIdentity`.

- [ ] **Step 1: Replace the old test with failing tests**

Delete `Declared_type_admission_retains_whole_type_without_inventing_a_serialized_profile_or_selected_type`. Add, using the existing `Host` and `Descriptor` helpers:

```csharp
[Fact]
public async Task Declared_type_describes_members_under_the_activity_value_profile()
{
    using var host = new Host([Descriptor("shop.address", typeof(SerializedAddress))]);
    var shape = (await host.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", [], new()))!;
    Assert.Equal(ActivitySerializedShapeState.Declared, shape.State);
    Assert.Equal("activity.shape.declared-type", shape.ReasonCode);
    Assert.Equal(ActivitySerializedValueKind.Object, shape.Kind);
    Assert.Equal(["city", "zipCode"], shape.Members.Select(x => x.SerializedName).Order());
    Assert.Equal(ActivityValueJsonProfile.Revision, shape.ProfileRevision);
    Assert.Equal("owner", shape.SourceContractRevision);
    var city = (await host.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", ["city"], new()))!;
    Assert.Equal(ActivitySerializedShapeState.Declared, city.State);
    Assert.Equal(ActivitySerializedValueKind.Scalar, city.Kind);
    Assert.Equal(0, host.RuntimeRegistryResolutionCount);
}

[Fact]
public async Task Declared_type_keeps_the_unbound_answer_when_no_members_can_be_described()
{
    using var host = new Host([Descriptor("shop.address", typeof(SerializedAddress)),
        Descriptor("shop.converted", typeof(ConverterPayload))]);
    var missing = (await host.Source.DescribeDeclaredTypeAsync(new("shop.address"), "owner", ["missing"], new()))!;
    Assert.Equal(ActivitySerializedShapeState.Unknown, missing.State);
    Assert.Equal("activity.shape.serialization-profile-unbound", missing.ReasonCode);
    Assert.Null(missing.Type);
    var dictionary = new TypeReference("shop.address", CollectionKind.Dictionary);
    var map = (await host.Source.DescribeDeclaredTypeAsync(dictionary, "owner", [], new()))!;
    Assert.Equal(ActivitySerializedShapeState.Unknown, map.State);
    Assert.Equal(dictionary, map.Type);
    Assert.Empty(map.Members);
    var opaque = (await host.Source.DescribeDeclaredTypeAsync(new("shop.converted"), "owner", [], new()))!;
    Assert.Equal("activity.shape.serialization-profile-unbound", opaque.ReasonCode);
    Assert.Empty(opaque.Members);
}

[Fact]
public async Task Declared_recursive_and_scalar_types_stay_bounded_and_member_free()
{
    using var host = new Host([Descriptor("shop.recursive", typeof(SerializedRecursive)), Descriptor("String", typeof(string))]);
    var recursive = (await host.Source.DescribeDeclaredTypeAsync(new("shop.recursive"), "owner", [], new(3, 5)))!;
    Assert.Equal(ActivitySerializedShapeState.Declared, recursive.State);
    Assert.InRange(CountMembers(recursive), 0, 5);
    Assert.InRange(Depth(recursive), 0, 3);
    var scalar = (await host.Source.DescribeDeclaredTypeAsync(new("String"), "owner", [], new()))!;
    Assert.Equal(ActivitySerializedShapeState.Declared, scalar.State);
    Assert.Equal(ActivitySerializedValueKind.Scalar, scalar.Kind);
    Assert.Empty(scalar.Members);
}
```

`ConverterPayload` is the existing `[JsonConverter]` fixture in `SerializedShapeFixtureActivities.cs`. If the `Host` admits types only through its capability catalog, admit both new aliases the same way the existing tests do.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test server/essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --filter "FullyQualifiedName~ActivitySerializedShapeSourceTests"`
Expected: the three new tests FAIL (`Declared` does not exist yet, then state is `Unknown`).

- [ ] **Step 3: Add `Declared = 4` to `ActivitySerializedShapeState` and its `PublicAPI.Unshipped.txt` line**

The doc comment on the member says: member names come from the declared type and are not proven for the stored value. Consumers that validate must treat it as unknown.

- [ ] **Step 4: Implement the declared branch in `ClrActivitySerializedShapeSource.DescribeDeclaredTypeAsync`**

After the existing admission check, for `declaredType.CollectionKind == CollectionKind.Single`, read `descriptors[0].ClrType` with `new ClrSerializedShapeReader(ownerContractIdentity, limits, cancellationToken).Read(clrType, declaredType, memberPath)`. A `Known` or `Truncated` result becomes `Declared` per the Interfaces block. Any other result, any other collection kind, and any exception matched by the existing `MetadataFailure` filter return today's unbound answer. Replace the comment above it, and the interface doc comment, with the new rule.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test server/essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj`
Expected: all PASS, including `Default_headless_projection_has_API_byte_parity...` and `API_and_endpoint_free_hosts_share_the_same_type_facts...`.

- [ ] **Step 6: Commit**

```bash
git -c core.fsmonitor=false add server/essentials/src/Activities/Design server/essentials/tests/Activities/Design
git -c core.fsmonitor=false commit -m "Describe a declared variable type's members as a Declared shape (#5676)"
```

### Task 2: Keep validation and the Activity Data picker unchanged

**Files:**
- Modify: `server/essentials/src/Workflows/Design/Api/Services/ActivityDataContextService.cs` (the `analysis` returned by `ResolveAsync`)
- Check, change only if a test fails: `server/essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Services/ActivityDataSelectionAnalyzer.cs`
- Test: `server/essentials/tests/Workflows/Design/Tests/ActivityDataSelectionAnalyzerTests.MemberReads.cs`
- Test: `server/essentials/tests/Workflows/Design/Api/Tests/Unit/ActivityDataContextEndpointTests.cs`

**Interfaces:**
- Consumes: `ActivitySerializedShapeState.Declared` and the task 1 answer shapes.
- Produces: `internal static ActivityDataSelectionAnalysis WithoutDeclaredMembers(ActivityDataSelectionAnalysis analysis)` in `ActivityDataContextService`, which rewrites every source shape whose `State` is `Declared` to `Unknown`, `Kind = Unknown`, no members/element/map value, reason `activity.shape.serialization-profile-unbound`, keeping `Type`, `IsNullable`, `IsSensitive` and `IsCredential`.

- [ ] **Step 1: Write the failing tests**

In `ActivityDataSelectionAnalyzerTests.MemberReads.cs`, with a fake `IActivitySerializedShapeSource` (follow the file's existing fake) that answers `DescribeDeclaredTypeAsync` with a `Declared` object shape for a variable `customer` of type `shop.address` (members `city`, `zipCode`), and the unbound Unknown answer for any other path:

```csharp
[Fact]
public async Task A_declared_variable_shape_produces_no_findings_and_stays_deferred()
{
    // member reads customer.city and customer.cty
    Assert.Empty(analysis.Findings);
    Assert.All(analysis.Reads, read => Assert.False(read.IsKnown));
}
```

In `ActivityDataContextEndpointTests.cs`, with the same fake registered:

```csharp
[Fact]
public async Task The_picker_receives_a_declared_variable_shape_as_unknown_without_members()
{
    var source = Assert.Single(response.Analysis!.Sources, s => s.Id == "variable:customer");
    Assert.Equal(ActivitySerializedShapeState.Unknown, source.Shape.State);
    Assert.Equal("activity.shape.serialization-profile-unbound", source.Shape.ReasonCode);
    Assert.Empty(source.Shape.Members);
}
```

Use the property names the existing models expose (`Reads`, `IsKnown`, the source id format); the assertions above fix the intent.

- [ ] **Step 2: Run them and confirm the picker test fails**

Run: `dotnet test server/essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~MemberReads"` and `dotnet test server/essentials/tests/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --filter "FullyQualifiedName~ActivityDataContextEndpointTests"`
Expected: the analyzer test PASSES already (the analyzer only trusts `Known`/`Truncated`); the picker test FAILS with state `Declared`. If the analyzer test fails, fix the analyzer so `Declared` behaves exactly like `Unknown`.

- [ ] **Step 3: Implement `WithoutDeclaredMembers` and apply it to the `analysis` in `ResolveAsync`'s success response**

The context revision is computed before this rewrite and stays as is.

- [ ] **Step 4: Run both test projects and confirm they pass**

Run: `dotnet test server/essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj` and `dotnet test server/essentials/tests/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git -c core.fsmonitor=false add server/essentials/src/Workflows/Design server/essentials/tests/Workflows/Design
git -c core.fsmonitor=false commit -m "Keep the Activity Data picker and validation unchanged for declared shapes (#5676)"
```

### Task 3: Label declared members in the expression authoring context

**Files:**
- Modify: `server/essentials/src/Workflows/Design/Api/Services/PersistedExpressionAuthoringContextSource.cs` (`ToShape`)
- Test: `server/essentials/tests/Workflows/Design/Api/Tests/Unit/PersistedExpressionAuthoringContextSourceTests.cs`
- Test: `server/essentials/tests/Expressions/Tests/ExpressionToolingProviderContractTests.cs`

**Interfaces:**
- Consumes: `ActivitySerializedShapeState.Declared`.
- Produces: `private static ExpressionValueShape ToShape(ActivitySerializedShape shape, string? declaredNote = null)`. A `Declared` root maps its `Kind` like `Known`, sets `HasLazyChildren = false`, and passes ``$"From the declared type `{shape.Type?.Alias}`; the stored value may differ."`` down so every `ExpressionValueMember` in its tree gets it as `Documentation`. Shapes with no `Declared` root are unchanged.

- [ ] **Step 1: Write the failing tests**

In `PersistedExpressionAuthoringContextSourceTests.cs`, register a fake shape source answering `Declared` for variable `customer` of `shop.address` with members `city` (scalar) and `address`-style nested object if the fake allows; otherwise `city` and `zipCode`:

```csharp
[Fact]
public async Task A_declared_variable_type_supplies_labeled_members()
{
    var symbol = Assert.Single(context.RootSymbols, s => s.Kind == ExpressionSymbolKind.Variable && s.Name == "customer");
    Assert.Equal(ExpressionValueKind.Object, symbol.ValueShape!.Kind);
    Assert.False(symbol.ValueShape.HasLazyChildren);
    var city = Assert.Single(symbol.ValueShape.Members!, m => m.Name == "city");
    Assert.Equal("From the declared type `shop.address`; the stored value may differ.", city.Documentation);
}
```

In `ExpressionToolingProviderContractTests.cs`, next to the existing `args.customer` test, a variable symbol with a labeled object shape:

```csharp
[Fact]
public async Task Typed_variable_members_complete_after_variables_dot_and_never_produce_member_diagnostics()
{
    // variable "customer", shape Object with member "city" (DisplayName "String", Documentation = label)
    var completion = await provider.GetCompletionsAsync(new(scope, "variables.customer.ci", new(0, 21)), CancellationToken.None);
    var item = Assert.Single(completion.Payload!.Items);
    Assert.Equal("city", item.Label);
    Assert.Equal("From the declared type `shop.address`; the stored value may differ.", item.Documentation);
    var validation = await provider.ValidateAsync(new(scope, "variables.customer.cty"), CancellationToken.None);
    Assert.Empty(validation.Payload!.Diagnostics);
    var hover = await provider.GetHoverAsync(new(scope, "variables.customer.city", new(0, 23)), CancellationToken.None);
    Assert.Contains("the stored value may differ", hover.Payload!.Documentation);
}
```

Also assert in the authoring test that a `Declared` scalar variable (alias `String`) gets no members and its shape has no documentation.

- [ ] **Step 2: Run them and confirm the authoring test fails**

Run: `dotnet test server/essentials/tests/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --filter "FullyQualifiedName~PersistedExpressionAuthoringContextSourceTests"` and `dotnet test server/essentials/tests/Expressions/Tests/Elsa.Expressions.Tests.csproj --filter "FullyQualifiedName~ExpressionToolingProviderContractTests"`
Expected: the authoring test FAILS (kind `Unknown`, lazy children, no label). The provider test is a pinning test and may pass already.

- [ ] **Step 3: Implement the `ToShape` change**

- [ ] **Step 4: Run all four affected test projects**

Run the four `dotnet test` commands from tasks 1–3 without filters.
Expected: all PASS.

- [ ] **Step 5: Commit, open the PR and review**

```bash
git -c core.fsmonitor=false add server/essentials/src/Workflows/Design server/essentials/tests
git -c core.fsmonitor=false commit -m "Label declared variable members in the expression authoring context (#5676)"
```

Push, open a draft PR closing #5676, then follow the `elsa-auto-review-merge` skill. Merge `main` into the branch before Ready (this adds public API).

### Task 4: Prove typed-variable completion in Studio (#5677)

**Files:**
- Test: `studio/src/essentials/Elsa.Studio.CodeEditor/Client/src/__tests__/toolingProjection.test.ts`
- Modify only if the test fails: `studio/src/essentials/Elsa.Studio.CodeEditor/Client/src/toolingProjection.ts` (`toDirectCompletion`)

**Interfaces:**
- Consumes: provider completion items with `label`, `detail` (member type display name) and `documentation` (the label), as produced after task 3.

- [ ] **Step 1: Write the test**

```ts
it("shows a typed variable's members with their type and the declared-type note", async () => {
  // provider returns [{ label: "city", detail: "String", kind: "property", insertText: "city",
  //   documentation: "From the declared type `shop.address`; the stored value may differ." }]
  // for source "variables.customer.ci" at its end
  expect(completions).toEqual([expect.objectContaining({
    label: "city", detail: "String", apply: "city",
    documentation: { markdown: "From the declared type `shop.address`; the stored value may differ." }
  })]);
});
```

Also assert that a provider returning no items for an unknown-shape variable yields an empty list. Follow the existing "prefers source-aware language-provider completion" test for setup.

- [ ] **Step 2: Run it**

Run: `pnpm --dir studio/src/essentials/Elsa.Studio.CodeEditor/Client test -- toolingProjection`
Expected: PASS (this pins existing pass-through). If it fails, fix `toDirectCompletion` and rerun.

- [ ] **Step 3: Prove it in a normally composed host**

Build Workbench and Studio from the branch (after task 1–3's PR is on `main`). Start them on free ports, never 5095. In Studio create a disposable workflow with a variable whose type is a catalog type with object members. If the Workbench catalog has no such type, stop and report it; do not add a sample type without the owner's yes. In a JavaScript input, type `variables.<name>.` in the compact editor and in the expanded editor, and capture screenshots of the suggestion list with its type and note. Also check that the Activity Data picker for that variable looks the same as on `main`.

- [ ] **Step 4: Commit, open the PR and review**

```bash
git -c core.fsmonitor=false add studio/src/essentials/Elsa.Studio.CodeEditor/Client/src
git -c core.fsmonitor=false commit -m "Pin typed-variable member completion in the Studio editor (#5677)"
```

Open a draft PR closing #5677 with the screenshots, then follow `elsa-auto-review-merge`.
