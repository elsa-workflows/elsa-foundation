# Worker HTTP starting profile

`worker-http@1` is a Foundation-reviewed starting selection for a single HTTP worker with durable workflow execution, event resumption, local filesystem locking and external bearer normalization. It selects [19 explicit feature IDs](../../specs/191-worker-http-profile/contracts/worker-profile.md), including identity abstractions, OIDC and the separate IAM EF feature. It contains no authority, audience, tenant, database connection, signing key or lock-directory value.

The profile is a pinned starting point. Individual additions/removals remain available, and the planner explains exact membership and missing required dependencies. The new catalog retains both earlier catalog pins and unchanged Embedded/diagnostics definitions. Package availability and runtime readiness require their own evidence.

## Developer flow

Create a new composition and inspect its exact expansion:

```bash
dotnet elsa composition init --profile worker-http@1 --output worker-composition.json
dotnet elsa composition plan --composition worker-composition.json --format json
```

After reviewing any individual edits, accept to a fresh file and generate a candidate for the chosen host shell/environment:

```bash
dotnet elsa composition accept --composition worker-composition.json --output accepted-worker.json
dotnet elsa composition generate --host-dir ./host --shell worker --environment Production --composition accepted-worker.json --output-dir ./candidate
```

The interactive prompts require typing `accept` and `generate`. Neither command overwrites an existing output. Without supplied inventory or persistence evidence, the plan retains `inventory-unverified` and `persistence-unverified`. Generation creates local files; it does not activate a shell, deploy packages or apply database migrations. The host must load the candidate's base configuration and the selected environment overlays, then run its ordinary activation/readiness gates.

Prepare the host source with feature settings as objects before generation. The file bridge preserves those existing local values while reconciling activation. It does not create arbitrary missing setting paths or accept secret/provider/connection values as portable feature patches. See the [file-bridge boundary](../../specs/176-composition-file-bridge/contracts/setting-review-v1.md).

## Host prerequisites

- Configure the existing [OIDC feature](../../src/essentials/Foundation/Identity/Oidc/README.md): nonblank Authority and independent Audience, fixed ProviderId/TenantId and opt-in NormalizeBearerClaims. Deployment metadata uses HTTPS. ClientId is unnecessary for a bearer-only worker; authentication defaults remain the host's choice.
- Initialize the ordinary persistence context for that same static tenant before shell activation. Incoming token claims cannot choose or overwrite that context. Configure legitimate IAM claim-mapping rules; a valid external token without a grant is forbidden by the existing permission policy.
- Configure a named Runtime persistence resource and its connection reference. Configure IAM's Provider/ConnectionName/connection and schema/migration provisioning independently; the Runtime default resource does not enroll IAM automatically.
- Supply a writable local `LocksFolderPath` and required Runtime signing/protection configuration. Local filesystem locking does not certify a distributed topology. Review migration policy before starting the host.

The authentication request/identity boundary is defined by [Spec190](../../specs/190-worker-oidc-normalization/contracts/bearer-normalization.md); this profile changes selection rather than authentication semantics. The [Embedded guide](embedded-runtime-profile.md) explains the common plan/accept/generate behavior and diagnostics group customization.

## Evidence boundary

[Spec191](../../specs/191-worker-http-profile/spec.md) requires actual built CLI commands, generated-file readback and fresh OS child activation with matching accepted IDs and consumed-file hashes. Its primary actor retains real JWT/HTTP execution, event resumption, durable IAM permission revocation and same-candidate restart. A separate edited candidate removes ControlFlow and changes Audience; paired authenticated capabilities requests must prove both file-driven changes.

The actor uses one isolated loopback issuer and two SQLite stores. It can prove handler wiring, candidate consumption and those durable local behaviors, but cannot certify an external IdP deployment, multiple tenants/providers, distributed hosts or deployed-package readiness. Consult the [implementation evidence](../../specs/191-worker-http-profile/implementation-evidence.md) for executed results and limitations. The full Ubuntu Runtime EF suite has executed all three Worker actor cases with no failures or skips on the qualified source recorded there. The ledger and [delivery issue #2326](https://github.com/elsa-workflows/elsa-foundation/issues/2326) record publication and resulting-main qualification separately from these local actor guarantees.
