# Worker profile and candidate consumption contract

Applies to Spec191 / #2326. Existing [selection planner](../../174-profile-selection-planner/contracts/selection-planner-v1.md), [CLI file bridge](../../176-composition-file-bridge/contracts/cli-file-bridge-v1.md) and [Spec190 bearer boundary](../../190-worker-oidc-normalization/contracts/bearer-normalization.md) remain authoritative; this profile adds no public command/identity protocol.

## Reviewed selection

Add the proposed immutable profile `worker-http@1` in a new bundled Foundation catalog snapshot. It must explicitly select exactly these 19 IDs:

- `ActivitiesControlFlow`
- `ActivitiesPrimitives`
- `ActivitiesRuntime`
- `ActivitiesSequence`
- `ApiCapabilities`
- `Events`
- `Expressions`
- `FileSystemDistributedLocking`
- `FoundationIdentityAbstractions`
- `FoundationIdentityOidc`
- `IdentityIamEntityFrameworkCore`
- `Mediator`
- `Primitives`
- `Serialization`
- `Tasks`
- `WorkflowsRuntimeApi`
- `WorkflowsRuntimeEntityFrameworkCore`
- `WorkflowsRuntimeResumption`
- `WorkflowsRuntimeTriggers`

Include a concise rationale and only source-verified dependency explanations. Keep the existing `embedded-runtime@1` and `diagnostics-ef@1` definitions unchanged. The current catalog v2 pin is `elsa-foundation@2`, digest `42afc8ccd1f18c972d882f1e214c961d5aad0788a6f0974e8964bb6e012bda15`; retain its resource and exact-pin resolution when publishing the next catalog version. Also retain catalog v1. Use v3 only if v2 is still the latest version when this work starts. A new version gets a new immutable catalog digest; do not edit a published version in place.

## Acceptance criteria

1. Add the new immutable catalog resource and keep exact-pin loading for v1, v2 and the new current version. Add regression coverage that the v1 and v2 catalog pins still resolve to their old snapshots, including the unchanged Embedded profile and diagnostics group digests. Unknown or mismatched pins must continue to resolve only to the current catalog as unresolved findings. Update current-catalog assumptions in existing tests to select Embedded by ID rather than requiring it to be the sole profile.

2. Exercise the actual built CLI process lifecycle with the bundled profile:
   - `composition init --profile worker-http@1 --output <authored.json>`
   - `composition plan --composition <authored.json> --format json`
   - interactive `composition accept --composition <authored.json> --output <accepted.json>`
   - interactive `composition generate --host-dir <source> --shell worker-oidc-runtime --environment <environment> --composition <accepted.json> --output-dir <fresh-candidate>`

   Reuse the existing CLI process and PTY helpers rather than copying their process code or calling the commands in-process. Assert the catalog and profile ID/version/digest pins plus the exact 19 IDs in authored, plan candidate/accepted, and accepted output. Read the generated base and selected environment overlay with `CshellsSourceReader` and assert its effective IDs equal the accepted selection. Preserve planner findings such as `inventory-unverified` and `persistence-unverified`; do not infer readiness from planning. Keep the candidate local and fresh, and leave source files unchanged.

3. Make the generated candidate drive the existing fresh-process Worker actor. Extend the current Worker child startup protocol to receive the generated candidate, selected shell and environment. Load the generated `shells.json` and selected `shells.<environment>.json` into the child CShell configuration used by `WithConfigurationProvider`. Remove the static `SelectedFeatures` activation list and its configuration loop. Keep `WithAssemblies(...)` as feature discovery only. The child must report the hashes of the generated files it consumed, and the test must compare those hashes to the candidate prepared by the CLI. Assert that actual `ShellSettings.EnabledFeatures` equals both the accepted IDs and the generated-file readback. Reuse the candidate for the existing child restart and assert the same candidate identity after restart. Do not introduce a second feature-selection list in host input or test configuration.

   Add one test-only candidate-consumption control without changing `worker-http@1`: derive a same-pin composition with an explicit `ActivitiesControlFlow` removal, run the real accept/generate commands into a second fresh candidate, then start a fresh child from those files and assert `ActivitiesControlFlow` is absent from actual `ShellSettings.EnabledFeatures` and the whole set matches accepted/source readback and consumed hashes. On this same secondary candidate, set the source OIDC Audience to an alternate nonblank value while leaving its accepted18 selection unchanged. Persist a legitimate `api-capabilities.read` grant for the configured actor; the stale startup `worker-api` token must return 401 with zero mapping reads/runtime rows, while a correctly signed token for the candidate audience must return 200 from the real `/capabilities` endpoint with one mapping read and zero runtime rows. This paired result must fail if the child keeps the old in-memory audience instead of consuming the generated candidate setting. This control only exercises the alternate candidate's authenticated read route; retain the exact-19 candidate for the event execution/resume/restart proof. The controls must fail if the old static feature list or any source-ignoring selection/settings bypass remains.

   Keep `Events` enabled in the secondary candidate: Serialization startup requires its `IInlineEventPublisher`. Removing Events would fail shell startup before the intended consumption assertions. The source review supports removing ActivitiesControlFlow for this capabilities-only control, but actual child startup and paired authorization remain required execution evidence.

4. Keep operator configuration out of immutable profile membership. The host still supplies a nonblank Authority, independent Audience, fixed ProviderId/TenantId, and normalization opt-in. Deployment HTTPS metadata remains required; `RequireHttpsMetadata=false` is only for the actor’s loopback issuer. Keep ClientId absent for this bearer-only actor and preserve host-owned authentication defaults. Root `AddPersistenceCore(configuredTenant)` remains before CShell activation; its ordinary scope must match the OIDC TenantId, and request token claims never choose it. The actor continues to use a named Runtime `primary` resource and separate host-owned database/lock/signing inputs.

   The IAM EF feature is a separate legacy target: configure its own Provider, ConnectionName and connection string, and retain schema/migration provisioning plus distinct Runtime/IAM store assertions. Do not imply that Runtime `primary` automatically enrolls IAM. Put OIDC/IAM feature values in the selected source `Features` entries before generation, with the IAM connection string in host `appsettings`; assert the actual child binds those values from the generated files while authored portable `settings` remains empty. Keep the feature entries present as objects so the bridge preserves their local settings while it reconciles the profile selection. Do not add a parallel in-memory `CShells:...:Features:<id>` provider that can select features independently of the candidate.

5. Retain the existing actual-handler and local-issuer proof: signature, issuer, audience and lifetime refusals; mapped forbidden and authorized execution; event resume; persisted revocation; and a fresh OS child process after restart. Keep route claims to the existing subset assertions: execute, stimuli and capabilities are present; design and publishing routes are absent. Do not claim an exact all-route inventory. The local issuer demonstrates handler and wiring behavior only, not external IdP interoperability.


## Proof ownership and refusals

Catalog-only assertions prove membership and immutable pin loading. CLI/PTY runs prove real producer artifacts and preserve unverified host/persistence findings. Generated-file readback proves selection in the candidate. Actual child receipts prove consumption and activation; authenticated requests and real store observations prove useful behavior. None alone proves deployed readiness.

The child reads all candidate settings from files. Root ordinary tenant input and fixture-only instrumentation are retained, but no independent CShells Features dictionary is permitted. The actual environment overlay must be loaded and hash-identified; receipts include no secret/connection values. A restart consumes unchanged files; source files remain byte-identical after generation. A source-ignoring selector or stale Audience must fail the respective modified-candidate actor control before restored gates are published.

Maintain all existing actor authentication, policy, tenant isolation, no-user/link-write and fresh-process objectives; changing fixture setup does not delete those controls. Negative tenant activation still uses mismatched ordinary host scope and must refuse before lookup. Every child/issuer and isolated file is deterministically cleaned up on failure.
