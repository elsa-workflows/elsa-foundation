# Research: Studio Feature Impact Review

**Source baseline**: `7fb745f8604b6ed67a2eae60266976c98bce949e`

**Specification**: [spec.md](spec.md), approved at `1d8215409fc114351ef60a03f955bfefa65975c6`

**Issue**: [#3074](https://github.com/elsa-workflows/elsa-foundation/issues/3074) and its root-reviewed readiness decision at [comment 6301690177](https://github.com/elsa-workflows/elsa-foundation/issues/3074#issuecomment-6301690177)

## Decision: Keep the existing cascade; add review provenance around the host draft

The feature module already has a single- and multi-seed cascade resolver. Enabling traverses required dependencies; disabling traverses required dependants. The resolver excludes optional edges. The toggle and bulk handlers know the direct seed IDs before they call it, but the resulting draft records only each feature's enabled state and configuration. The host state is already kept separately for Studio and Server.

The review will compare each host's loaded baseline with its current full draft. Retain effective direct enabled-state roots per host, not only the most recent operation. A state root is effective only while its requested state remains in the final desired draft and differs from its baseline; a root returned to baseline or overwritten by a later cascade is pruned or marked inapplicable. For a changed feature reached by one or more effective roots, derive each required-edge path from the loaded catalog and show every applicable root. Reconcile applicability after every existing cascade so a superseded root cannot classify or explain current impact. Separately retain direct setting edits while their fields still differ from baseline, even when an enabled-state root for that feature has been superseded. Omit any feature whose desired state/configuration is again equal to baseline, and do not let a net-zero feature remain as an impact cause solely because an earlier action touched it. Keep the existing resolver as the only enable/disable behavior; review derivation explains its result and does not apply a second cascade.

Concrete regression: baseline `A=off`, `B=on`, with required edge `A → B`; disable B, then enable A. The resulting draft is `A=on`, `B=on`. B is omitted as net-zero, and the earlier `(B, off)` root is inapplicable and absent from every surviving classification/reason. If a direct setting edit on B still differs from baseline, keep that setting change independently visible.

**Alternatives considered**:

- Replacing the current resolver with a new graph/closure implementation was rejected: it could make the review and the applied draft disagree and would expand the feature beyond the issue's reuse-current-contract boundary.
- Showing only the latest toggle's roots was rejected: pending edits from earlier operations and overlapping bulk seeds would lose their explanation.
- Showing every feature encountered during prior actions was rejected: reverted or net-zero operations are no longer part of the current desired state and should not appear as impact.
- Retaining an overwritten enabled-state request as an applicable root was rejected: it does not describe the current draft and can create a stale or contradictory explanation. Setting-field provenance remains independent because those values may still be changed.

**Evidence**: `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/module.tsx` around lines 71–93 (host-separated state), 147–239 (required/optional cascade), 241–258 (Studio/Server targets), and 385–476 (single/bulk edits and reset). Existing rendered tests already cover the single cascade, optional exclusion, one bulk disable, filter behavior, and host separation in `Client/src/__tests__/module.test.ts`.

## Decision: Bind each review to the full selected-host snapshot

A current review is the selected host, complete desired state, loaded feature catalog/dependency snapshot, and the configuration revision paired with that snapshot. Any draft mutation, host selection change, refresh, or catalog replacement invalidates it. Apply uses that same complete state and revision only while the review is valid. Cancel returns to the unchanged local draft and performs no save. Reset affects only the selected host's local draft and clears the prior review.

The existing backend revision protects feature configuration. It does not atomically detect package/manifest dependency metadata that changes out of band after Studio loaded the catalog. Refreshing or replacing the catalog requires a new client review, but the current backend contract does not provide a stronger graph revision. The UI and qualification must not claim otherwise.

**Alternatives considered**:

- Adding a server-issued impact token, graph revision, new endpoint, or cluster-wide preview was rejected as outside scope and unnecessary for the client review contract in Spec996.
- Applying the latest live draft after a review was rejected because it would let the target or payload differ from what the operator inspected.

**Evidence**: `module.tsx` around lines 128–141 (full desired-state Apply payload and dirty comparison), 341–383 (per-host catalog load and Apply), and 465–476 (host-local reset). Backend behavior is preserved and covered by `Essentials/tests/Modularity/Tests/FeatureManagementServiceTests.cs` and `JsonShellFeatureConfigurationStoreTests.cs`; their existing revision applies to the shell feature map. The source-readiness audit is `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3074-source-readiness-audit.md`.

## Decision: Use existing public auth hooks and preserve anonymous/demo access

The current module marks both targets writable unconditionally. Elsa.Studio.Web already exposes `useAuthSession` and `usePermissions` through `@elsa-workflows/studio-sdk`. The host's existing `useHostControlAccess` behavior enforces manage permission only for an authenticated session; it leaves anonymous/auth-disabled demo use writable and keeps the server-side gate authoritative. The extension will use only public SDK hooks, apply the existing `module-management:manage` rule to stage, reset, and Apply controls for authenticated sessions, and keep inspect access consistent with the existing Read permission.

Tests should mount the feature page under the public auth provider (including the public anonymous provider for demo coverage) and verify the visible/disabled controls and unchanged Apply behavior. The app-private host-control helper must not become a new package dependency.

**Alternatives considered**:

- Checking for the manage permission for every session was rejected because an anonymous/auth-disabled demo session has no permissions and would become incorrectly read-only.
- Reusing `Studio/src/apps/Elsa.Studio.Web/Client/src/app/useHostControlAccess.ts` from the extension was rejected because it is app-private and would create cross-project coupling.
- Changing the Studio SDK or backend permission policy was rejected; the existing public hooks and authorization contract are sufficient.

**Evidence**: `Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts` re-exports `../auth`; that package publicly exports `useAuthSession`, `usePermissions`, `AuthProvider`, and `AnonymousAuthProvider`. `useHostControlAccess.ts` enforces permissions only for `session.status === "authenticated"`. Permission names live in the existing host control implementation; the backend remains the authority.

## Decision: Preserve backend error meaning rather than infer it from status alone

The current module prefixes every HTTP 409 as a stale feature catalog. The existing backend also uses 409 for an activation-guard refusal, so the status alone is not proof of a stale revision. Preserve the backend-provided reason and render the operation as failed; do not report success or relabel every 409 as stale. No error shape or server behavior changes are needed.

An Apply failure after the backend has saved configuration is also possible if a later descriptor refresh or shell reload fails. The client must not claim the host configuration is unchanged merely because it received an error; the error remains a failure, and a fresh catalog read is needed to reconcile the persisted state.

**Evidence**: `module.tsx` around lines 359–383 (Apply error path) and 1288–1303 (`getErrorMessage`); `Essentials/tests/Modularity/Tests/FeatureManagementServiceTests.cs` covers save-before-refresh/reload failure outcomes. The approved spec requires visible reasons and forbids false success; it does not promise rollback.

## Decision: Report only known reload implications

Setting metadata already includes a `restartRequired` flag. Review may surface known implications from that existing metadata and the selected host's current contract, but must mark absent knowledge as unknown. The existing Apply result can report zero reloaded shells, so neither a preview nor success message should promise a reload/activation beyond available evidence. No provider, runtime feature, or extra host is to be enabled for the proof.

**Evidence**: `Client/src/studio-sdk.d.ts` `StudioSettingDescriptor.restartRequired`; `module.tsx` `FeatureApplyResult` and its Apply status; `specs/993-studio-diagnostics-index/quickstart.md`'s normal private host profile.

## Decision: Qualify actual host boundaries using the canonical private-host guide

Component tests supplement, but do not replace, proof in an actual Elsa.Studio.Web process addressing the actual Elsa.Workbench Server target. Reuse the bounded private-root, copied-configuration, fresh database/key-ring, launch, sign-in, and cleanup instructions in [Spec993's composed Workbench guide](../993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison). Use the normal supplied modules, a fresh browser context, and the standard Development UI sign-in from the copied source configuration. Do not edit shared configuration, copy a personal session, create a custom identity endpoint, add a provider, or activate an optional provider.

For both Studio and Server targets, perform a review and Cancel and prove neither private host file changed. Then perform a reversible Apply for that target and verify only its corresponding copied `shells.json` changed. Choose only an already-loaded, non-provider change that cannot remove Feature Management, authentication, or the API needed to complete the proof; if the actual catalog offers no such bounded choice, stop for root review instead of changing host composition. Dispose of the private run root through the guide's guarded cleanup. Receipts must omit credentials, tokens, cookies, headers, and configuration values.

Existing component tests cover authenticated read-only state; the standard development seed is used for the actual normal-host Apply journey. Do not create an Identity administrator or infer a read-only account from the normal seed. If a read-only session is already available in the isolated private seed, it may supplement the component proof; otherwise keep it at the SDK-authenticated component boundary and existing backend authorization tests.

## Integration boundary

The implementation is based on `7fb745f8604b6ed67a2eae60266976c98bce949e`. Main subsequently advanced to `73a4f8bf` in a separate Connections-only change; the feature module, auth SDK, bootstrap, and package pins were not changed there. Root owns reconciliation against the then-current main in one serial integration lane. There is no dependency on #3131 or #3132, and this worker does not integrate or qualify other work.
