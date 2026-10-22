# Quickstart: proving spec 265

Build and test only the touched projects, through the build-slot wrapper (see AGENTS.md).

## 1. Placement and publication

```bash
dotnet test Essentials/tests/Workflows/Publishing/Api --filter "FullyQualifiedName~ActivityTemplatePlacement|FullyQualifiedName~PlacedReferenceRemapper"
```

```bash
dotnet test Essentials/tests/Activities/Graph --filter "FullyQualifiedName~GraphActivityProvider"
```

Expect the following:
- A body result reference publishes and carries placed ids.
- Two placements get independent ids.
- Each `activity.graph.body-*` code has a refusal test.

## 2. Runtime (US1–US5)

```bash
dotnet test Essentials/tests/Activities/Graph
```

```bash
dotnet test Essentials/tests/Activities/Behavioral --filter "FullyQualifiedName~GraphBoundary"
```

Expect the following:
- "Add two numbers" with `Sum` computed by a body node returns `5` for `2 + 3`. The caller has its own `A = 99`.
- A graph variable read sees the graph's value, not a caller variable with the same name.
- A body capture is visible to a later node and to the output mapping.
- Each of these still holds after resume.
- A nested graph reads its own inputs.

## 3. Non-regression (FR-011)

```bash
dotnet test Essentials/tests/Workflows/Runtime
```

Also run the Flowchart reusable-boundary tests:

```bash
dotnet test Essentials/tests/Activities/Flowchart --filter "FullyQualifiedName~ReusableBoundary"
```

## 4. Elsa 3 (US7)

```bash
dotnet test Extensions/Elsa3/tests/Mapping
```

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration --filter "FullyQualifiedName~ImportedReusableRuns"
```

`reusable-reads-input`, `reusable-reads-result` and `reusable-captures-variable` all run. `reusable-sets-variable`
is still refused (D-7).

## 5. Studio (US6)

From `Studio/`:

```bash
pnpm --filter @elsa-workflows/studio-workflows test
```

```bash
pnpm typecheck
```

```bash
pnpm test:browser -- activity-definition-graph-authoring
```

In the browser, build "Add two numbers" by picking `A` and `B` in a body node, publish it and run a caller.
