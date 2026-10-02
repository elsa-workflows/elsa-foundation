# Validation guide: Worker OIDC bearer normalization

This is a planned validation guide for the subsequent implementation. New tests named below are not present or executed by the specification leaf. [Contract](contracts/bearer-normalization.md) and [proof matrix](contracts/acceptance-proof-matrix.md) define expected outcomes.

## Prerequisites

Use the repository SDK/build-slot wrapper. Check `uptime` before diagnosing timing-shaped failures. Run affected projects serially on this shared machine. Do not add a test project, provider matrix or cadence.

The new shared Worker fixture owns an isolated local issuer/discovery/JWK signing key, short-lived test tokens, explicit IAM SQLite target, named Runtime SQLite target, writable single-host lock directory and host signing prerequisites. It starts actual HTTP hosts, initializes the actual shell ordinary persistence context to the configured nondefault tenant before store resolution (existing AddPersistenceCore(defaultScope: tenant) or equivalent fixed host-owned accessor), provisions each owned schema through the existing module mechanism, seeds persisted artifacts/mappings through supported store contracts, and resets mutable state per scenario. Use idiomatic async teardown for servers/temp databases/keys; avoid tracing raw tokens or retaining request artifacts. A fresh host instance/process for restart must reopen the same databases rather than reuse an in-memory service provider.

## Reusable host entrypoints

Register the fixed host `AddPersistenceCore(configuredTenant)` before `AddCShells` captures root descriptors. The existing [shell activation host](../../tests/essentials/Foundation/Identity/Tests/ShellActivationHost.cs) documents inheritance into the shell provider; later parameterless persistence registrations use TryAdd and must preserve that scope. Seed mappings through the activated shell's `IClaimMappingStore`, not a direct DbContext bypass. Use explicit IAM Provider/ConnectionName with `ConnectionStrings:Iam` for its separate file, alongside the named Runtime resource; IAM is outside automatic resource enrollment.

The existing [Worker HTTP fixture](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerHttpFixtureHostEvidenceTests.cs) supplies real execute/stimulus and runtime-store setup. Replace its fabricated root authentication seam with shell-owned Foundation Identity/OIDC middleware; do not layer real JWT proof over its fake handler. The [shared persistence restart control](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs) demonstrates disposing one host and reopening the same SQLite file; extend cleanup and reopening to the explicit IAM file. This source preflight identifies reusable entrypoints, not an executed OIDC actor result.

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
5. Reuse that exact valid token after saving the rule with empty GrantPermissions:403 on the later execution request. Restart the host against the same databases and verify the removed grant, completed workflow state, absence of user/link writes and unchanged external TokenRefreshBoundary metadata.
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
