# Context lifecycle contract

This records the behavior to qualify for #3439. It does not freeze new API shapes before the experiments or expand M1's accepted minimum.

1. A request carries explicit source/correlation, attachments and its current active surface. The surface does not grant permission or change older operations.
2. Admission returns the sanitized evidence actually accepted, with diagnostics. The client must not label its pre-admission snapshots as provider evidence.
3. Installed sources own resource interpretation, authorized reads and trusted guidance. Shared code enforces ownership, transport bounds, policy, cancellation and consistent failure handling.
4. Before provider disclosure, current identity, module and policy restrictions apply to retained evidence/results/history. If an owner cannot establish authorized disclosure, withhold or refuse explicitly. Preserve accepted receipts and do not reexecute effects.
5. Required missing evidence blocks provider use. Optional failure remains visible without discarding unrelated complete evidence. A configured bound and Stop terminate collection even for noncooperating asynchronous contributors; late completion cannot change accepted state.
6. Shared chips use declared metadata only. Resource payloads and applied-result rendering remain opaque and module-owned.
7. Both existing adapters receive the same admitted tools/refusal causes. Domain-specific filters and resource switches in adapters are prohibited.

Implementation tasks update this note with actual signatures/wire deltas, defaults and compatibility evidence. Update the owning extension catalog and public API baselines for each actual externally visible change.


## Implemented boundary, under package qualification

- `IAgentContextProvider.AdmitDisclosureAsync(AgentContextDisclosureRequest)` reuses the existing named fan-in. Default support fails closed. The host stamps original `ContributorId`; exact source/module/kind/schema must remain installed.
- `AgentToolDescriptor.DisclosureSourceId` is frozen into preparation and its review digest. The engine stamps result `Access` with owner, contract, original target, source and invocation identity. Result-only turns route to that exact source; a same-module decoy is not selected.
- Source success authorizes original historical evidence/results unchanged, while its returned context is the current permitted projection. Current host policy checks original retained evidence as well as the projection. Unsupported, missing or failed disclosure gates cannot become optional collection warnings.
- Before every delegated callback or host batch operation, current module admission and the admitted tool set are checked again. A narrower projection cannot retain an old tool scope. Authorized detail targets may be added while retaining the original target/revision. A changed resource identity/revision in this accepted turn requires fresh intent; the captured callback cannot silently follow it.
- The shared provider dispatch boundary journals genuine results before handing them to either provider loop, independently of assistant text. The existing session store must preserve the result, context, fingerprint and provenance; missing metadata stops disclosure. Legacy history that cannot establish provenance requires a fresh supported session. No second result store or orchestration engine is added.
- New requests carry current `activeSurface`; admitted message context and `ContextAdmitted` stream events replace displayed candidates. Shared chips inspect declared metadata only. Provider adapters remain resource-agnostic.
- Server `AgentContextOptions.SourceTimeout` and the client collector's `contributorTimeoutMs` default to10seconds per source. Backend descriptors default required; the existing frontend `required` marker governs client outcomes. Cancellation does not wait for asynchronous source cooperation. These bounds do not sandbox arbitrary installed code that blocks synchronously.
- A receipt may identify a newly created resource after a collection target. Shared code validates generic owner/preparation identity; the module owns before/after resource semantics. Display labels/routes do not redefine resource identity.

Most shared API shapes are additive. The public `DefaultWorkflowAgentContextProvider` constructor changes from `(IWorkflowRevisionProvider, IWorkflowActivityCatalogProvider)` to `(IWorkflowRevisionProvider, IWorkflowActivityCatalogProvider, IAgentSessionService, IAgentToolAuthorizer)`. This is a source and binary compatibility break: direct consumers must rebuild and supply the owning session and native authorization services, or use the supported workflow feature registration to resolve them through DI. An authorization-free legacy constructor would undermine the disclosure boundary and is not retained.

There is also a deliberate pre-GA behavior change: contributions without the new disclosure support now refuse provider use rather than reuse unproven evidence. Custom session stores must retain the new journal provenance. A release would require the appropriate breaking-change classification; this work performs no release or publishing. Core and Workflows are not currently enrolled in the repository's text API baseline mechanism; the enrolled `Elsa.Agent` public feature/registration API is unchanged. The [live domain catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) owns current extension-point documentation.

Tool catalog discovery validates that every declared disclosure source ID resolves uniquely to an installed source owned by the same module, before a tool is advertised or prepared. This uses public descriptor metadata in the existing named tool aggregator; collection and current-authority behavior stay on the context fan-in. Registration metadata does not establish current authority or guarantee that a future result will be admitted. A reviewed operation whose owner disappears receives an explicit executor-unavailable refusal before module execution, rather than a false uncertain-effect report.
