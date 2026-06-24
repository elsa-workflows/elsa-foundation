# Quickstart: Secrets Module

## Prerequisites

- .NET SDK for `net10.0`.
- `pnpm` for Foundation Studio client modules.
- A local Elsa Server shell with Expressions, Secrets, Secrets API, and a persistence provider enabled.
- A local Foundation Studio shell with the Secrets Studio module enabled.

## Backend Build And Tests

From `elsa-foundation`:

```bash
dotnet build Elsa.Server.slnx
dotnet test tests/Elsa/Secrets/Tests/Elsa.Secrets.Tests.csproj
```

Expected outcome:

- Secrets projects build.
- Lifecycle, resolution, API, persistence, feature registration, and no-reveal tests pass.

## Studio Build And Tests

From `elsa-foundation-studio`:

```bash
dotnet test tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj
pnpm --filter @elsa-workflows/studio-secrets test
pnpm --filter @elsa-workflows/studio-secrets build
```

Expected outcome:

- Secrets Studio module manifest registration test passes.
- Client module tests pass.
- Client bundle builds.

## Manual End-To-End Scenario

1. Start the local Elsa Server with Secrets enabled.
2. Start Foundation Studio against that backend.
3. Open `/security/secrets`.
4. Create a text secret named `smtp-password` in the encrypted store with value `super-secret-123`.
5. Verify the list and detail views show metadata but not `super-secret-123`.
6. Open a workflow activity input that supports the secret picker.
7. Select `smtp-password` and save the workflow definition.
8. Inspect the saved workflow definition payload.
9. Verify it contains a `Secret` expression with `name: smtp-password` and does not contain `super-secret-123`.
10. Execute or resolve the input through runtime materialization.
11. Verify the consumer receives the resolved value.
12. Rotate `smtp-password` to `new-secret-456`.
13. Execute or resolve again and verify the new value is used without editing the workflow.
14. Revoke `smtp-password`.
15. Execute or resolve again and verify a safe resolution failure.

## Safety Checks

Search captured test logs, API responses, saved workflow definitions, and audit records for submitted raw values:

```bash
rg "super-secret-123|new-secret-456" tests src specs
```

Expected outcome:

- Matches are limited to tests/spec fixtures that deliberately define the submitted value and never to metadata responses, persisted workflow definitions, logs, or audit records.
