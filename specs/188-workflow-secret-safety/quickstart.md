# Validation journey (planned implementation)

This specification task changes no production code. The commands below become meaningful in the slice that
introduces the code they exercise ([delivery slices](tasks.md#delivery-slices)); they are planned, not results.

## For workflow authors: using a secret in phase 0

- Bind the secret where it is used. In the input's syntax picker choose `Secret` and pick the secret; the definition
  stores only the reference, and the value is read when the activity runs. `SendHttpRequest`'s `Authorization`
  input is the built-in example: bind it to a secret holding the whole header value, such as `Bearer <token>`; it
  replaces any `Authorization` entry in `RequestHeaders`.
- A secret cannot be put into a workflow variable, or reused through one, in phase 0. Set Variable and a variable's
  initial value refuse a secret reference at publish (`VF-ACT-012`), and the annotated built-in activities cannot hand a resolved secret on
  through their result. A third-party or future activity that echoes a string input is not refused in phase 0 unless
  its author marks that input as refusing secret references. When two activities need the same secret, bind the secret reference on each input; each binding
  resolves on its own when its activity runs.
- A secret reference fits only a single text or any-typed input. Publish refuses it on a number, date, list,
  `Object`, `JsonElement` or `JsonObject` input (`VF-COER-001`), and on inputs an activity echoes or persists, such as `Inline`'s expression or
  `WriteHttpResponse`'s body (`VF-ACT-012`).

## Working rules for every slice

- One branch per slice, cut from current `main`. Push to `origin` (`elsa-workflows/elsa-foundation`, or
  `elsa-workflows/elsa-foundation-studio` for slice 10), never to `main`. Open the PR ready for review, not as a
  draft. Comment the claim on the slice's issue before writing code, and re-check the issue and open PRs before
  pushing.
- Build and test only the projects the slice touches. A `[dotnet-build-slots] waiting for a build slot` line is a
  queue, not a hang; allow for it in timeouts. Check `uptime` before trusting a timing-shaped failure.
- Never run `dotnet test --no-build` after a failed build: it runs the previous assembly. Compare executed against
  discovered tests (`--list-tests`) when a suite reports green with fewer tests than expected.
- A slice that adds or changes a `ProjectReference` or project refreshes maps and lock files:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- check
```

  Stage every changed map by explicit path, `docs/maps/manifest.json` included. Update `packages.lock.json` for new
  projects per [NuGet lock files](../../docs/reference/nuget-lock-files.md). Never `git add -A`; after committing,
  `git ls-tree` the new project folders to confirm every source file is tracked.
- No benchmark, performance test or performance gate (#1668, ADR 0073).

## Bite-proof procedure (every behavioral claim)

1. Run the slice's key test green.
2. Copy the protecting source file aside (`cp file /tmp/...`), apply the single revert or mutation named in the
   [acceptance matrix](contracts/acceptance-proof-matrix.md), rebuild that project, rerun the test: it must go red.
   Do not use `git stash` (the stash stack is shared across worktrees).
3. Restore the file from the copy, rebuild, rerun green.
4. Record the mutation, the red result and the restored green result in the PR body. A test that stayed green
   under its mutation is reported as unguarded, not hidden.

## Per-slice journeys

### Slice 1: spec 079 housekeeping

```bash
grep -rn "ISecretResolver\b\|src/Elsa/Secrets" specs/079-secrets-module   # expect no output
```

### Slice 2: secret bindings compile and persist as withheld references

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/essentials/Activities/ControlFlow/Tests/Elsa.Activities.ControlFlow.Tests.csproj
dotnet test tests/essentials/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj
dotnet test tests/essentials/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj
dotnet test tests/essentials/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
```

Journey: compile a node whose credential-shaped input is bound to `Secret`, materialize its snapshot, and confirm the
serialized snapshot holds the withheld envelope and no value; activation refuses it with `VF-ACT-010`.

### Slice 3: activation-time resolution and failure semantics

```bash
dotnet test tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```

Journey: with a fake `IRuntimeSecretResolver`, run the activity, read the persisted activity state and confirm the
withheld envelope; suspend, change the fake's value, resume and confirm the new value reached the activity; make the
fake fail with each code and confirm the fault and its retryable flag; remove the fake and confirm the activity waits
with an activation-failure incident.

### Slice 4: Secrets bridge

```bash
dotnet test tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj
dotnet test tests/essentials/Secrets/Tests/Elsa.Secrets.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
```

Journey: create a secret through `ISecretManager` under two tenants with the same name, run each tenant's instance,
rotate, revoke and delete, and check each run's outcome against [the resolution contract](contracts/runtime-secret-resolution.md).

### Slice 5: input sensitivity declaration and effective policy

```bash
dotnet test tests/essentials/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/essentials/Activities/Design/Api/Tests/Elsa.Activities.Design.Api.Tests.csproj
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
```

### Slice 6: credential-literal rule at seven entry points

Journey: push one definition with a literal on a credential input through every Design API entry point, promote
included (400 from admission, 409 when the draft changed after admission), through file reconciliation and git export
(that item refused, the pass completes), and through publish, also for an activity the catalog does not hold yet;
confirm `git diff` for the slice touches under `src/essentials/Workflows/Design/Persistence/` only the promote
precondition (`IPromoteDraftToVersionCommand.cs`, `WorkflowDraftChangedException.cs`, `WorkflowDraftStateHash.cs`,
`EfWorkflowDesignCommands.cs`)
and adds no rule there.

```bash
dotnet test tests/essentials/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj
dotnet test tests/essentials/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj
dotnet test tests/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
```

### Slice 7: withholding backstop and leak surfaces

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/essentials/Workflows/ExecutionEvidence/Tests/Elsa.Workflows.ExecutionEvidence.Tests.csproj
dotnet test tests/essentials/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj
```

### Slice 8: masking

```bash
dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
```

### Slice 9: canary end to end

```bash
dotnet test tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj --filter "FullyQualifiedName~Canary"
```

Run once green, then once per protection id in A15 (P1 to P9, M1 to M6) with that protection disabled, in the
scenario A15 names, following the bite-proof procedure. Injection scenarios use only the canary host's test-only DI
replacements. Paste the table into the PR body, one row per id; a green row blocks the slice.

### Slice 10: Studio (in `elsa-foundation-studio`)

```bash
pnpm install
pnpm --filter @elsa-workflows/studio-web test
pnpm --filter @elsa-workflows/studio-workflows test
pnpm typecheck
pnpm lint
```

### Slice 11: SendHttpRequest consumes a secret (owner decision)

```bash
dotnet test tests/essentials/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj
dotnet test tests/essentials/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
dotnet test tests/essentials/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj
dotnet test tests/essentials/Secrets/Workflows/Tests/Elsa.Secrets.Workflows.Tests.csproj --filter "FullyQualifiedName~SendHttpRequestSecretEndToEnd"
```

Journey: bind a secret to `SendHttpRequest.Authorization`, run against the recording local endpoint, confirm it
received the value as the `Authorization` header and answered without repeating it, rotate and run again, then scan
every surface with the canary scanner. A design catalog reconciled by a dev build from before this slice throws
`ActivityVersionHashMismatchException` for `SendHttpRequest`; recreate it (research R17, no migration).

## Final gates (last slice of the backend)

```bash
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- check
git diff --check
```

The PR that merges the last slice sets `specs/188-workflow-secret-safety/spec.md` to `Implemented`
([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
