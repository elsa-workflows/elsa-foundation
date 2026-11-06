# Release source capture extension points

## IReleaseCaptureService

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseCaptureService`.
- **Kind:** Replacement; exactly one selected implementation per shell.
- **Default/lifetime:** Internal scoped `ReleaseCaptureService`, registered by inheritable
  `WorkflowReleaseCaptureFeature` with virtual configuration and post-configuration methods.
- **Purpose:** Resolve exact source strategies, hold all owner callbacks through complete actual-fact verification,
  ZIP creation and confirmed write-once archive acceptance. Require a durable, schema-ready selected archive.
- **Failure:** Value-free `ReleaseCaptureException` for invalid requests/source contracts or semantic refusal. Located
  semantic diagnostics remain available on the exception. Provider failures and ambiguous archive commits propagate
  through their existing boundaries; reconcile an ambiguous commit using the same capture ID and archive reader.
- **Known implementations:** `ReleaseCaptureService` *(intra-domain — default)*.

## IReleaseCaptureSource

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseCaptureSource`.
- **Kind:** Exact selection-kind strategy; duplicate/invalid claims fail when the capture service resolves.
- **Composition/lifetime:** Host-selected scoped contributions; the orchestration uses no temporary service provider.
- **Purpose:** Read an exact immutable owner selection and its owned associations under its real acquisition/retention
  boundary. Invoke and await one callback while all supplied content remains readable and retained. The callback can
  build and durably archive the complete multi-owner release. Never retry it or expose a lazy reader after it returns.
- **Authority:** Source authorization and qualified environment/tenant/partition context come from the owning host.
  An embedded source-scope claim grants no read permission. The generic service neither resolves latest versions nor
  creates draft/publication state.
- **Failure:** Cancellation and retention/store failures propagate. Missing callbacks, repeated callbacks, incomplete
  root counts, conflicting same-key content and aggregate work/transport limits refuse successful creation.
- **Known implementations:** No source adapter is installed by this feature. Native, BPMN and ETL adapters are separate
  owner contributions; standalone transport/verification can be composed without source capture or archive persistence.

## IReleaseSourceMemberContributor

- **Contract:** `Elsa.Workflows.Publishing.Releases.Core.IReleaseSourceMemberContributor`.
- **Kind/lifetime:** Scoped fan-in, keyed by unique adapter identity. Several contributors may inspect the same
  container codec. This feature installs none; module owners register their own contributions.
- **Purpose:** Expand exact retained containers into members without selecting roots or declaring graph facts.
  Each adapter/member key and payload digest is processed once, in deterministic bounded batches. Keep each callback
  open through the final owner inspection and archive commit when its members require source retention.
- **Failure:** Invalid or duplicate claims, changed same-key bytes/headers, canceled acquisition, missing/repeated/detached
  callbacks, and member/byte/round/retention limits refuse successful creation. The service drains pending callbacks
  before returning a failure and preserves actual archive commit uncertainty.
- **Authority:** A captured reference is no permission grant. Each owner applies its required read/export policy before
  resolving exact referenced material. Final independent verification must establish closure and all immutable facts.
- **Known implementations:** Optional Runtime and module-owned source contributors; none are installed by this feature.

The service orders source acquisition by ordinal selection-kind identity and nests at most 32 owners. Member, byte,
root and inspection budgets apply across all contributions, including repeated declarations. Equal shared members must
agree on their full descriptor, owner version and original bytes. Every required member is supplied before verification;
there is no source/target fallback while building the manifest.

There are no events, startup tasks or recurring tasks. A complete archived transport remains independently re-verifiable;
archive persistence itself does not assert semantic validity, origin authenticity, target readiness or serving authority.
