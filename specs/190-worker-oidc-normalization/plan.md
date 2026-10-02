# Implementation Plan: Worker external bearer normalization

**Branch**: `codex/2304-worker-oidc-normalization-spec` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Task #2304; specification authoring only. Adapter implementation remains a subsequent leaf after review and publication gates.

## Summary

Extend the existing `Elsa.Foundation.Identity.Oidc` module with default-false `NormalizeBearerClaims`, independent `Audience`, and declarative exposure of existing `ProviderId`/`TenantId`. Keep the actual JWT bearer handler. When explicitly enabled, install a guarded scoped bearer-events adapter that loads owned rules, filters incoming internal claims, invokes `IClaimsNormalizer` and validates its single-identity result before publication. Use a distinct adapter-owned normalized authentication type and enroll it only with the installed guarded path; the raw validated-token identity type must differ. Preserve existing permission policy and request cancellation behavior.

## Technical Context

**Language/Version**: C# / repository .NET10 target.
**Primary Dependencies**: Existing CShells metadata/binding; Foundation Identity/Core; ASP.NET Core JwtBearer/OIDC/options. No new identity platform or package family.
**Storage**: Existing `IClaimMappingStore`; first real acceptance uses `EfClaimMappingStore` with explicitly configured IAM SQLite target and host-owned migrations. Runtime state uses existing named SQLite resource resolution independently.
**Testing**: Existing Identity tests for registration/trust/callback contracts; Runtime EF tests for one real local issuer/HTTP/database journey; existing IAM tests and Architecture gates for regression. No new test project or provider matrix.
**Target Platform**: One from-source single-host Worker on supported .NET host platforms. Actual execution evidence names its platform; local issuer is not deployed IdP interoperability.
**Project Type**: Additive modular host authentication adapter inside the existing OIDC owner package.
**Performance Goals**: None added; retired performance measurement is not restored by this unit.
**Constraints**: Default-false compatibility; one static namespace; no user/link writes; request-scoped rule loading; fixed diagnostics; no bypassable trust enrollment.
**Scale/Scope**: One supported bearer scheme/provider/tenant per opt-in composition. No distributed topology, dynamic tenant derivation or interactive normalization.

## Constitution Check

Pre-design and post-design review use ratified framework §§2.1/2.3/2.7 (Core/implementation/dependency boundaries), §2.6.2 (replacement safety), §2.16 (stable package ownership), §§2.21/2.23 (retained tests and focused registration/logic proof), §2.22 (feature/catalog maintenance) and repository Spec151 FR-024 (normalization/failure/cancellation).

- Implementation stays in the existing OIDC owner package; Core gains no JwtBearer or EF dependency. It consumes `IClaimMappingStore` and `IClaimsNormalizer` contracts rather than reaching into IAM implementations.
- No duplicate normalizer/evaluator/IAM model. Incoming internal-claim filtering belongs to the OIDC boundary; do not change shared `DefaultClaimsNormalizer` behavior for legacy callers.
- The existing rule-store replacement guard remains applicable. The normalizer has no replacement marker/guard today; opt-in activation requires exactly one normalizer descriptor, without changing the shared legacy seam. Adapter activation validates its actual collaborators and scheme/event ownership; it does not introduce a collection of competing normalizers or silent last-write-wins trust.
- Existing feature selection/settings metadata supplies declarative opt-in. Profiles select membership only; no new settings inheritance, authorization grants, issuer or secret in a profile definition.
- Keep retained tests and legacy public contracts. New behavior requires meaningful unit/registration controls plus actual HTTP/JWT/database actor and mutation proof, and the owning README/extension-point catalog.
- Framework §2.24 / Elsa §E2.9 provisional material is not a new gate or ratification in this unit.

No exception is requested. Authoring these documents is not an implementation gate pass.

## Project Structure

```text
specs/190-worker-oidc-normalization/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  checklists/requirements.md
  contracts/bearer-normalization.md
  contracts/acceptance-proof-matrix.md
src/essentials/Foundation/Identity/Oidc/
  OidcAuthenticationFeature.cs, OidcAuthenticationOptions.cs
  ConfigureOidcOptions.cs
  Extensions/OidcAuthenticationServiceCollectionExtensions.cs
  (new) OidcBearerNormalizationEvents.cs
  (new) OidcBearerNormalizationOptionsValidation.cs
  (new) README.md; owning ../Core/EXTENSION_POINTS.md
 tests/essentials/Foundation/Identity/Tests/
  OidcAuthenticationRegistrationTests.cs, OidcAuthenticationFeatureTests.cs
  (new) OidcBearerNormalizationTests.cs
 tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
  WorkerHttpFixtureHostEvidenceTests.cs (retained prior evidence)
  (new) WorkerOidcBearerHostEvidenceTests.cs
  (new) WorkerOidcBearerHostFixture.cs
```

New file names are the implementation allocation, not existing classes. Reuse issuer/setup/reset helpers within existing test assemblies; do not clone substantial arrange blocks. Only add test project references needed for the real actor host.

## Phase 0 — Research decisions

See [research.md](research.md) for source evidence, event bypasses, selected opt-in surface, final-options validation and deferred boundaries. Resolve framework ordering against the installed version before implementing. No material product choice is left open for this fixed layout.

## Phase 1 — Design and contracts

The [bearer contract](contracts/bearer-normalization.md) owns configuration, named-option/event ordering, request trust transition, diagnostics and failure semantics. The [data model](data-model.md) owns captured namespace/rules/principal transitions. The [proof matrix](contracts/acceptance-proof-matrix.md) maps every FR/SC to actual or adverse proof. The [quickstart](quickstart.md) lists existing-project checks and the real host journey; commands become runnable when the implementation exists, and are not recorded as executed now.

## Actual root and shell ownership

The real Worker acceptance selects `FoundationIdentityAbstractions`, `FoundationIdentityOidc` and the explicit IAM EF feature in the shell alongside the Runtime closure. `FoundationIdentityAbstractionsFeature.UseMiddleware` owns shell authentication/authorization after request services are selected. Root hosting supplies only necessary ASP.NET marker/routing services; it must not pre-authenticate using a fabricated grant or enroll a raw root scheme. Observe which provider actually resolves the handler, normalizer and mapping store during the request. Service-registration parity is a separate control, not permission to bypass declarative shell activation in the actor proof.

Final trust validation must run for ordinary hosts and actual shell activation. Use the existing Identity activation pattern (`IHostedService` plus `IShellInitializer`) rather than assume root-only ValidateOnStart runs for shell services. Resolve the selected scheme/options during that gate, before request serving; validate both supported entry points in the acceptance host.

## Implementation sequence

1. Add settings/audience fallback and focused legacy/bearer-only registration controls.
2. Install opt-in events and final named-options validation, bind the distinct exact trust type, enforce input filtering and output validation with cancellation/failure controls.
3. Prove all success-short-circuit hooks and replacement/registration ordering through the real handler; do not stop at direct event invocation.
4. Build one explicit IAM plus named Runtime SQLite host with isolated real issuer/discovery/signing key, then execute the actor and durable-rule/restart/no-user-write journey.
5. Run restored adverse mutations, affected suites, architecture/maps and diff review. Publish exact-head evidence and verify resulting-main gates before claiming adapter delivery.
6. Only then consider a separate Worker profile publication leaf with exact membership/prerequisite evidence.

## Integration and release

One root-owned integration leaf. The specification PR references #2304, leaves Status Draft until its authoring acceptance is reviewed, and does not check implementation tasks or claim adapter behavior. After specification publication passes its gates, create one coherent adapter implementation issue with native parent1961 and prerequisite2304, using these tasks/proofs. Do not create one GitHub issue per task or presume that passing the adapter certifies profile publication.
