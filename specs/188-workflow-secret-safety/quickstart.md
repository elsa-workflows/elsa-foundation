# Validation journey (planned implementation)

This specification task changes no production code. The commands below become meaningful in the slice that
introduces the code they exercise ([delivery slices](tasks.md#delivery-slices)); they are planned, not results.

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

```bash
dotnet test tests/essentials/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj
dotnet test tests/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/essentials/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj
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

Run once green, then once per protection with that protection disabled (A15), following the bite-proof procedure.
Paste the table into the PR body.

### Slice 10: Studio (in `elsa-foundation-studio`)

```bash
pnpm install
pnpm --filter @elsa-workflows/studio-web test
pnpm --filter @elsa-workflows/studio-workflows test
pnpm typecheck
pnpm lint
```

## Final gates (last slice of the backend)

```bash
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- check
git diff --check
```

The PR that merges the last slice sets `specs/188-workflow-secret-safety/spec.md` to `Implemented`
([spec lifecycle](../../docs/reference/spec-lifecycle.md)).
