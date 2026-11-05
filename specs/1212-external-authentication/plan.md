# Implementation plan: external authentication and OIDC connections

**Branch**: `agent/codex/3579-external-auth` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Status**: implementation in progress. Owner selected provisioning policies assigning saved existing Elsa roles; recurring external group-to-role mappings are excluded for now.

## Summary

Deliver #3579 within parity #2771, including necessary #2848 identity isolation. Add host-wide connection management, explicit overrides, real OIDC admission/sign-in, non-provisioning preview, links/sessions/recovery and Studio together. Finish with actual provider, restart/multi-node, review/merge and resulting-main proof. A stage or draft PR does not finish the objective.

## Technical context

- C#/.NET from `global.json`, ASP.NET Core, IdentityModel/OpenIddict, CShells and existing IAM/provider EF persistence; TypeScript/React/Studio SDK tooling.
- Reuse Identity.Core, Secrets.Core, normal cookie/token pipeline and host Data Protection. No production credentials/configuration.
- Baseline options plus opt-in bounded global catalog; separate atomic flow/preview/session aggregates. Initial multi-node proof uses shared PostgreSQL or SQL Server; SQLite proves single-node restart. Additional engine claims require actual evidence.
- xUnit Identity/EF tests, composed hosts, controlled OIDC provider, Studio/browser journeys, shared/native persistence, packed/public consumers. Respect build slots and shared-machine load.
- Initial database catalog bound 256 records; bounded/pageable reads and remote call/state limits. No arbitrary throughput promise.

## Constitution check

Read framework v6.1.0, Elsa v6.3.0 and the contribution review checklist. No constitution amendment or use of provisional workflow/activity semantics.

- Contracts/models stay in Identity.Core; protocol/EF/UI dependencies stay outside. Internal implementations unless genuine extension points; provider-family inheritance only; bridges consume Core.
- Scoped logic, immutable singleton values only; explicit replacement diagnostics and named contribution contracts for live discovery.
- CQS, explicit privileged global scope after authorization, atomic create/consume and expected revisions; no literal package versions. Update API tracking/catalogs together.
- Risk-based identity/grant/secret/concurrency/recovery regressions. No derived TDD cadence exists; record that gap and do not infer universal test-first. Use regression before/after evidence where practical.
- No new domain, runtime/Design coupling or audit system. New tests enter appropriate CI tier; whole-solution testing stays CI unless necessary.
- Recheck after contracts/integration; independent correctness, standards and acceptance review under current rules.

## Project structure

- `Essentials/src/Foundation/Identity/Core/`: scoped subjects and connection/admission/session/flow contracts.
- `Essentials/src/Foundation/Identity/Elsa.Foundation.Identity/`: shared identity defaults.
- `Essentials/src/Foundation/Identity/ExternalAuthentication/Core/` and `ExternalAuthentication/Elsa.Foundation.Identity.ExternalAuthentication/`: optional managed-connection contracts and policy/registry/catalog behavior. This dependency envelope consumes Secrets.Core and persistence scope contracts without adding them to every base Identity.Core consumer. Managed protocol code belongs to an optional ExternalAuthentication/Oidc adapter; existing direct bearer consumers acquire no Secrets dependency.
- `Essentials/src/Foundation/Identity/ExternalAuthentication/Api/`: optional backend protocol/preview/management web feature and live discovery source. Base ASP.NET Core Identity renders method choices and implements the Core canonical-user session issuer, without taking a dependency on managed authentication/Secrets.
- `Essentials/src/Foundation/Identity/Oidc/`: retain direct bearer validation. `Identity/ExternalAuthentication/Oidc/`: optional managed protocol adapter consuming the managed Core contracts and installed OIDC validation libraries.
- `Essentials/src/Foundation/Identity/AspNetCoreIdentity/`: admission/session bridge and canonical user projection.
- `Essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/`: catalog reuse, exact links, atomic admission/session stores and migrations.
- `Essentials/src/Foundation/Identity/ExternalAuthentication/Persistence/EntityFrameworkCore/`: optional dedicated flow/preview artifact authority and four-provider migration history. This keeps Secrets/managed-auth dependencies out of base IAM persistence.
- `Essentials/src/Foundation/Identity/Api/`: existing provider-neutral bootstrap/session/token protocol. Optional managed operations stay in `Identity/ExternalAuthentication/Api/` so base API consumers do not acquire managed-authentication/Secrets dependencies.
- `Extensions/Identity/studio/`: optional external-auth React module and standalone SDK following Connections/Secrets conventions.
- `Essentials/tests/Foundation/Identity/`, feature UI tests and real-provider harness: behavior/integration/qualification.
- `docs/reference/authentication-architecture.md`, `identity-configuration.md`, ADR 0085, parity goal/registry and catalogs: final behavior and operations.

## Stages and dependencies

1. Foundation/security: typed subject across issue/refresh/logout, canonical projection, #2848 isolation proof; then exact links, atomic admission and durable flow/session contracts. R5 preserves subject isolation; managed sessions project saved Elsa grants only.
2. Connections/ownership: baseline/catalog CAS, explicit override lifecycle, validation/Secrets, role delegation and final-login guard. Confirm role authority before implementing dependent behavior.
3. Real login/preview: protocol validation, durable browser state, normal cookie/token completion and isolated preview. Preserve bearer/optional compositions.
4. Operations/API/Studio: editor/test/preview/enable, links/sessions, notifications and recovery; source/ownership/permission/theme/responsive states.
5. Qualification/delivery: two real provider choices, all admission/role/source transitions, redaction, preview counts, revocation, last-path races, restart/shared two-node journeys. Pack/public consumers, current required gates, exact-head review/merge, resulting-main repetition and scoped closeout.

See [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md) and [tasks.md](tasks.md). No narrower milestone substitutes for complete acceptance.
