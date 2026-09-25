# Research: File-deployed composition activation and recovery

Status: design evidence for #2036 at source baseline `90a0b398f11c80374c7bbe0e341259acd74666b7`. These are bounded decisions for a Workbench-style default shell, not a released host-control API.

## 1. Candidate and deployment are different authorities

**Decision:** Build on the delivered [file bridge](../176-composition-file-bridge/contracts/file-bridge-v1.md). Its generated directory is a complete, reviewed local candidate. A separate deployment process must attest which complete artifact it switched into the selected host. Foundation does not copy files into the running host or use the legacy feature editor to apply resources.

**Evidence:** [`CompositionGenerateCommand`](../../src/essentials/Cli/CompositionGenerateCommand.cs) opens an explicit local source, builds a candidate, requires a human diff decision and publishes to a fresh directory through [`CompositionFilePublisher`](../../src/essentials/Cli/CompositionFilePublisher.cs). Publication rechecks all copied source files, stages private files and moves the completed directory. The bridge's private change tokens last for one invocation and are neither portable nor a durable deployment compare-and-swap. The [JSON feature store](../../src/essentials/Modularity/Api/Services/JsonShellFeatureConfigurationStore.cs) revises only `Features`, not root resources or overlays. The [apply/recovery investigation](../../docs/reports/runtime-composition/apply-recovery-boundary.md) contains the focused post-save fault evidence.

**Alternative rejected:** Reuse `POST /modularity/features/apply` for whole-bundle activation. Its save precedes catalog refresh and reload, writes one feature node directly, and can return a zero reload count after a saved change. It cannot give a truthful bundle revision or rollback guarantee.

## 2. A portable digest over secret-bearing files is not the handoff identity

**Decision:** The shareable handoff uses a newly allocated opaque candidate ID, safe file-role inventory, source selection, accepted catalog/feature pin, and unresolved findings. A trusted local verifier may retain a private content check for every included file to detect drift. Do not emit raw file bytes, raw unknown values, physical paths, bridge change tokens, or an unkeyed per-file/content hash of secret-bearing configuration in the portable handoff. An external deployer supplies its own release/artifact identity and expected-current check.

**Rationale:** The source bundle can contain connection values and unknown settings. Publishing an unkeyed digest over such material can turn a low-entropy value into an offline guessing oracle when the rest of the file is known. A random candidate ID alone is not integrity proof; the private verifier must recheck the complete artifact at handoff and the deployer must attest what it actually switched. Reopening an artifact in a later process without a trusted private record requires a fresh review. Repeated reads of the *same reviewed artifact* keep its ID; independently regenerated identical bytes need not reuse it.

**Alternative deferred:** A content-addressed public release ID. It is attractive for reproducibility, but its disclosure and canonicalization rules need a dedicated threat review before it is safe for secret-bearing configuration.

## 3. Existing host observations prove shell lifecycle, not exact candidate match

**Decision:** Use the existing authenticated root management reload as the host-control seam and distinguish its per-shell result from transport success. For v1, restrict readiness verification to the configured Workbench default shell. A candidate is not called active unless a host-produced observation can bind its exact reviewed bundle to the reported generation; until that seam is proven, report `active generation observed; candidate match unverified`.

**Evidence:** Workbench [loads base and selected overlay JSON before environment and command-line overrides](../../src/apps/Elsa.Workbench/Program.cs). It maps the [CShells management API](../../src/apps/Elsa.Workbench/Program.cs) behind the server-side management key from [ADR 0037](../../docs/adr/0037-studio-management-bridge-keeps-host-management-key-server-side.md). The pinned `CShells.Management.Api` package documents `POST /reload/{name}`, `GET /{name}` and a per-shell reload response with success, new generation, drain and error. Its `GET /{name}` response also contains blueprint `ConfigurationData` verbatim, which can include secrets; a browser or shareable diagnostic must not proxy that payload. The [Workbench readiness endpoint](../../src/apps/Elsa.Workbench/Readiness/ShellReadinessEndpointExtensions.cs) names only its configured default shell and returns the active generation when ready. The existing [real-registry test](../../tests/essentials/Modularity/Tests/ServerReadinessTests.cs) proves a failed initializer leaves the previous generation ready.

**Gap:** The pinned CShells `ShellDescriptor` exposes name, generation, creation time, and static blueprint metadata, but the current management response carries no loaded-bundle digest or effective-source attestation. Static blueprint metadata is copied onto each generation, so it cannot by itself prove that changed files or process overrides were used. A directory scan and a successful reload response cannot establish candidate-to-generation equality. The management package's exact HTTP status on a per-shell activation error has not been proven from its XML; inspect the response body instead.

**Follow-up gate:** Before any story claims `candidate active`, run a focused host-attestation spike. Test whether a Workbench-owned, secret-safe generation marker can be derived from the exact selected source bundle during candidate construction, carried through successful promotion, and read back with the active generation. Include process overrides, file changes during reload, two shells, failed initializer, and canary secrets. If the marker cannot prove exact source use, keep candidate match unverified and revise the product claim. This is not a reason to expose the raw blueprint payload.

**Alternative rejected:** Compare candidate files to a local deployment directory after reload and infer the host loaded them. Runtime process overrides and package generation make that directory a source snapshot, not host truth; see the [selected-host evidence boundary](../../docs/reports/runtime-composition/selected-host-evidence-boundary.md).

## 4. Failure and recovery need readback, not blind replay

**Decision:** Model `candidate-only`, `deployment-observed`, `reload-attempted`, `active-generation-observed`, `candidate-match-verified`, `refused`, and `uncertain` separately. After an exception/timeout, inspect external deployment identity and active shell generation before retry. A repair or source rollback is a new reviewed operation; neither action implies database or migration rollback.

**Evidence:** [`FeatureManagementService.ApplyAsync`](../../src/essentials/Modularity/Nuplane/Services/FeatureManagementService.cs) saves before refresh and reload. The new [fault probes](../../tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs) pin exception and zero-count windows. The [runtime management contract](../173-shared-persistence/contracts/runtime-management.md) already requires checking per-shell success and previous-generation retention instead of treating HTTP 200 as proof. `#1902`, `#1895`, `#1900`, `#1145`, and `#1951` remain separate dependency gates.

**Alternative rejected:** Auto-retry the same candidate after any failed response. It may already have changed deployed source or promoted a generation; the old review/expected-current context can be stale.

## 5. First implementation boundary

**Ready to scope:** A file-only candidate handoff that records safe roles, opaque identity and local drift checks, with no live-host or persistence-safe claim. It can be tested in CLI/Planning fixtures and builds on the already delivered bridge.

**Needs host-attestation proof before implementation promise:** A server-side verifier that claims the reviewed candidate is the active generation. Existing host APIs can prove reload result, active generation and default-shell readiness, but not exact candidate matching. The next implementation issue for full activation should follow the host-attestation gate; it must not silently downgrade the success criterion.

**Out of scope:** Foundation-owned atomic multi-file switch, generic resource editor, browser-held key, non-default-shell readiness, arbitrary host layouts, multi-shell transactions, data migration or rollback.
