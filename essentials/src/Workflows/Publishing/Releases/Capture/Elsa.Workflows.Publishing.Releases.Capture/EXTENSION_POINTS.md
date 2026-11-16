# Release source capture extension points

## IReleaseCaptureService

- **Contract:** `Elsa.Workflows.Publishing.Releases.Capture.Core.IReleaseCaptureService`.
- **Layer:** Core contract; optional Publishing capture orchestration.
- **Signature:** `CreateAsync(captureId, selections, options, cancellationToken)` returns `ValueTask<ReleaseCaptureReceipt>`;
  exact types/defaults are in [the contracts](../Core/ReleaseSourceCaptureContracts.cs).
- **Override when:** Replacing orchestration while retaining complete source lifetimes, independent verification and confirmed durable acknowledgement.
- **Dependent callers:** Source creation hosts and capture tests; source API/Studio integration is a later slice.
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

- **Contract:** `Elsa.Workflows.Publishing.Releases.Capture.Core.IReleaseCaptureSource`.
- **Layer:** Core strategy contract; implementation belongs to the source owner bridge.
- **Signature:** `ExecuteAsync(selections, limits, capture, cancellationToken)` returns `ValueTask`; the callback receives
  `ReleaseSourceCapture` and its lifetime token. Exact types are in [the contracts](../Core/ReleaseSourceCaptureContracts.cs).
- **Implement when:** Exporting an exact supported owner selection under its actual retention and authorization boundary.
- **Dependent caller:** `ReleaseCaptureService` after exact selection-kind dispatch.
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

- **Contract:** `Elsa.Workflows.Publishing.Releases.Capture.Core.IReleaseSourceMemberContributor`.
- **Layer:** Core fan-in contract; implementations belong to the referenced member owners.
- **Signature:** `ExecuteAsync(containers, captured, limits, capture, cancellationToken)` returns `ValueTask`; the callback
  receives an `IReleaseContentReader` and lifetime token. Exact types are in [the contracts](../Core/ReleaseSourceCaptureContracts.cs).
- **Implement when:** Supplying exact additional members referenced by a supported captured container.
- **Dependent caller:** `CaptureReleaseSourceMembers` within the retained capture service.
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

## ReleaseSourceMembersCapturing

- **Contract/layer:** Sealed `IEvent` in the optional Capture.Core package, [payload](../Core/ReleaseSourceMembersCapturing.cs).
- **Publisher:** `ReleaseCaptureService` through `IInlineEventPublisher`; sequential, awaited, default ThrowImmediately.
- **Aggregation:** One scoped action-named `CaptureReleaseSourceMembers` handler resolves all scoped
  `IReleaseSourceMemberContributor` registrations and holds their callbacks through `CompleteAsync`.
- **Registration:** `WorkflowReleaseCaptureFeature` uses canonical idempotent `TryAddEventHandler`; hosts select
  Events and can add independent audit handlers or normal pipeline middleware without replacing aggregation.
- **Payload/observation:** Get-only retained roots and limits; rich completion context is an exactly-once awaited
  callback. Missing, repeated or detached completion refuses acknowledgement. Original callback failures, including
  unknown archive commits, take precedence over protocol or source failures. A later audit/middleware failure may
  leave a complete archive; reconcile the same capture ID rather than assuming absence or retrying business work.

There are no startup tasks or recurring tasks. A complete archived transport remains independently re-verifiable;
archive persistence itself does not assert semantic validity, origin authenticity, target readiness or serving authority.
