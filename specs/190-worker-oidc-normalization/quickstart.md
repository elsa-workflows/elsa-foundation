# Validation guide: Worker OIDC bearer normalization

This is a planned validation guide for the subsequent implementation. New tests named below are not present or executed by the specification leaf. [Contract](contracts/bearer-normalization.md) and [proof matrix](contracts/acceptance-proof-matrix.md) define expected outcomes.

## Prerequisites

Use the repository SDK/build-slot wrapper. Check `uptime` before diagnosing timing-shaped failures. Run affected projects serially on this shared machine. Do not add a test project, provider matrix or cadence.

The new shared Worker fixture owns an isolated local issuer/discovery/JWK signing key, short-lived test tokens, explicit IAM SQLite target, named Runtime SQLite target, writable single-host lock directory and host signing prerequisites. It starts actual HTTP hosts, initializes the actual shell ordinary persistence context to the configured nondefault tenant before store resolution (existing AddPersistenceCore(defaultScope: tenant) or equivalent fixed host-owned accessor), provisions each owned schema through the existing module mechanism, seeds persisted artifacts/mappings through supported store contracts, and resets mutable state per scenario. Use idiomatic async teardown for servers/temp databases/keys; avoid tracing raw tokens or retaining request artifacts. The required restart terminates and awaits the first child OS process, then starts a distinct child from the same compiled fixture/configuration against the same database files. Record both process identities and the first exit before second startup. Keep the parent issuer/key and exact valid token alive across restart; a new in-process host or service provider is insufficient.

## Reusable host entrypoints

Register the fixed host `AddPersistenceCore(configuredTenant)` before `AddCShells` captures root descriptors. The existing [shell activation host](../../tests/essentials/Foundation/Identity/Tests/ShellActivationHost.cs) documents inheritance into the shell provider; later parameterless persistence registrations use TryAdd and must preserve that scope. Seed mappings through the activated shell's `IClaimMappingStore`, not a direct DbContext bypass. Use explicit IAM Provider/ConnectionName with `ConnectionStrings:Iam` for its separate file, alongside the named Runtime resource; IAM is outside automatic resource enrollment.

The existing [Worker HTTP fixture](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerHttpFixtureHostEvidenceTests.cs) supplies real execute/stimulus and runtime-store setup. Replace its fabricated root authentication seam with shell-owned Foundation Identity/OIDC middleware; do not layer real JWT proof over its fake handler. The [shared persistence restart control](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs) supplies file reopening/pool cleanup patterns, but its same-process restart is not FR-011 proof. [WorkbenchProcess](../../tests/essentials/Workbench/Tests/WorkbenchProcess.cs) supplies real child/Kestrel lifetime patterns, but hardcodes Workbench and cannot initialize this nondefault root scope. The existing [CLI executable fixture](../../tests/essentials/Cli/Fixtures/ResourceAwareLiveHost/Elsa.Cli.Fixtures.ResourceAwareLiveHost.csproj) and its build-only parent references show a non-test host allocation. None supplies the entire required actor.

Allocate one fixture-only `Fixtures/WorkerOidcHost/WorkerOidcHost.csproj` beside the existing Runtime EF Tests area, with executable output, IsTestProject=false and IsPackable=false, and build it through that existing test project. Its Program composes real Kestrel/shell OIDC/IAM/Runtime with the fixed root scope. The parent WorkerOidcHostFixture handles issuer/key/token, process lifecycle and bounded safe receipts. Use a private fixture control channel for startup inputs, schema/artifact/rule setup and activated-store observations; actor requests use production HTTP routes. Do not retain raw sensitive output or place credentials/tokens/connections in argv, and do not introduce product configuration APIs for test-only needs. Assertions and discovery remain in the existing suite. This source preflight is an implementation allocation, not an executed OIDC actor result.

## Focused developer loop after implementation

```bash
dotnet test tests/essentials/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --filter 'FullyQualifiedName~OidcBearerNormalization|FullyQualifiedName~OidcAuthentication'
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter 'FullyQualifiedName~WorkerOidcHostTests'
```

Run actual-handler controls as well as direct adapter units. Run independent direct-construction stubbed tests for every branch of each new or modified logic-bearing class, plus direct feature registration/resolution. Verify the named-options/EventType replacement guards, distinct raw/normalized types and all three event success bypasses. Mutation controls must affect production validation/normalization and be restored before the full affected suites.

## Real actor journey

1. Bind explicit opt-in OIDC configuration with local authority, API Audience and fixed provider/tenant; omit ClientId. Activate the same declarative feature closure used for the accepted Worker scenario. Select FoundationIdentityAbstractions/FoundationIdentityOidc and explicit IAM persistence in the shell; use its middleware and actual request services, without a root fabricated principal. Observe actual scheme/defaults, no unintended interactive handler, mounted routes and both store targets.
2. With no authorization header, tampered/wrong-issuer/wrong-audience/expired tokens, request execute:401, zero mapping/normalizer calls and no execution row.
3. With a valid token but no legitimate rule, including forged internal claims and cross-namespace rules, request execute:403, no execution row and no user/link writes. Also activate with a mismatched persistence context and inject conflicting/global/privileged/across-scope request contexts: refuse before mapping access, without overwriting context.
4. Seed an owned external-claim mapping for `workflow-runtime.execute` and any actual stimulus permission required by the mounted endpoint. Send a legitimately mapped token to the existing real execute/stimulus paths. Observe suspension/bookmark, resume and completion in Runtime SQLite. Do not assume one permission authorizes every route; derive required permissions from the actual endpoint contracts.
5. Reuse that exact valid token after saving the rule with empty GrantPermissions:403 on the later execution request. Await the first child exit and start the second child from the same built artifact/configuration against the same databases. Observe the removed grant and completed workflow state through the new process, repeat execution with the exact still-valid token and require403, verify absence of user/link writes and unchanged external TokenRefreshBoundary metadata.
6. Exercise store/normalizer exceptions, malformed outputs, evaluator failure and actual request abort. Match the proof matrix, capture only stable result codes, and require no successful partial ticket or new workflow effect after cancellation observation.

## Full affected gates after restoration

```bash
dotnet test tests/essentials/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj
dotnet test tests/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Retained OpenIddict composition controls and Workbench host checks must be included when the final changed integration boundary affects them; inspect actual project locations/CI selection before reporting. Run relevant backend `e2e-tests/` against a rebuilt server with a fresh database if implementation changes runtime/stimulus behavior. This adapter should preserve those behaviors, and its new actor fixture still exercises their real HTTP path.

For this docs-only authoring PR, run structural/cross-reference/diff review and generated-map freshness; CI supplies existing applicable gates. Do not invent new actor passes before code exists. Refresh maps only deliberately, explicitly stage all changed map files including manifest, and review generated findings. PR and resulting-main checks require exact source identity; a later retry does not establish a causal repair of existing incidents.
