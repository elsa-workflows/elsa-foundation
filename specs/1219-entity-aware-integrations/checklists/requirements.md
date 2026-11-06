# Specification Quality Checklist: Entity-aware integrations

**Purpose**: Validate the provisional product specification before its review; this is not implementation acceptance.
**Created**: 2026-11-06
**Feature**: [spec.md](../spec.md)
**Status**: Draft — specification quality reviewed; catalog boundary agreed; full-feature dependencies and delivery remain open

## Content Quality

- [x] No implementation prescriptions for languages, frameworks, wire interfaces or storage representation.
- [x] Focused on user value and business needs: permitted discovery, source-owned actions, real approval and reliable recovery.
- [x] User scenarios and outcomes are written for authors, source administrators, operators and integrators.
- [x] All mandatory template sections are complete and in the required order.

## Requirement Completeness

- [x] No unresolved owner-question markers remain; confirmed service-connection and record-follow-on choices are linked.
- [x] Requirements are testable and unambiguous within provider-declared capability profiles.
- [x] Success criteria define verifiable complete journeys, two workflows/tenants/connections, exact values and zero unauthorized disclosure/writes.
- [x] Success criteria describe externally observable outcomes rather than an implementation stack.
- [x] Acceptance scenarios cover all seven prioritized journeys; A01–A13 map the required evidence categories.
- [x] Edge cases include identity/schema changes, revocation, ambiguous writes, duplicates, outage, reordering and feedback.
- [x] Scope is bounded to external entity-aware integrations; #3916 owns the optional record backend, with no current implementation lane.
- [x] Dependencies, assumptions, source authority and open agreement gates are identified explicitly.

## Feature Readiness

- [x] All functional requirements have acceptance coverage in the journey scenarios and finite A01–A13 matrix.
- [x] User scenarios cover discovery, authoring/read/action, approval, privacy, event recovery, projections and extensibility.
- [x] The specified behavior aligns with the measurable outcomes; no execution of those outcomes is claimed.
- [x] Existing evidence and architecture dependencies are contextual, not a newly selected production mechanism or public ABI.

## Agreement and Delivery Gates

- [x] Root and independent acceptance/correctness review at `4b08750f64eaa9728cbbcece94328d7789a0c48e`; FR-033 closes the start-only gap.
- [x] Owner approval of the catalog boundary at `1bf3d5955a42222a5b62e4b0d463e17c43eedd16`, recorded in [the receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3923#issuecomment-6675758688); this authorizes the scoped catalog plan, not the full feature.
- [ ] M1 real provider/service-credential and complete reference/security/recovery qualification.
- [ ] #3917 tenant-preserving publication merge/resulting-main qualification and complete catalog disclosure implementation/qualification.
- [ ] Shared Runtime external-event responsibility agreed with the #3912/#3904 owners; no proposed ABI adopted silently.
- [ ] Explicit workflow/workload source-use authority and current-versus-retained event grant profile.
- [ ] Applicable domain/package admission, projection-owner contracts and initiative-owned architecture agreement.
- [ ] Production implementation, independent distributable-SDK/provider proof, composed UX, required review/checks/merge and resulting-main qualification.

## Notes

- Validation iteration 1: the product choices are resolved. Remaining issues are explicitly assigned architecture/provider qualification gates, not questions requiring routine owner interruption.
- Validation iteration 2: root review clarified interactive versus explicitly authorized workload access, untrusted snapshots versus legitimate retained Runtime grants, and the still-unagreed event grant policy. Fresh source reads/operations retain current authorization; accepted work cannot disappear silently.
- Validation iteration 3: canonical #3608 requires entity events to start or continue automation. FR-033, US5 scenarios 6–8, SC-005 and A08 now require authored waits/correlation and restart-qualified resumption of two existing workflows for one record, foreign source/tenant isolation, independent responsibility and duplicate protection for consumed/later waits. Recipient-selection and retained-authority semantics remain subject to the shared Runtime agreement; start-only evidence cannot pass.
- Validation iteration 4: the owner approved the catalog security boundary after reviewing draft #3923 at `1bf3d5955a42222a5b62e4b0d463e17c43eedd16`. Scoped catalog planning may proceed; shared Runtime ingress/workload authority, full provider/UX acceptance and the remaining feature units are not inferred approved or complete.
- The specification uses existing product/module names and an evidence table to preserve source provenance and dependency status. It does not prescribe a language, framework, endpoint schema, storage product or new Runtime contract.
- Preliminary real-source, public input seam and synthetic catalog witnesses remain distinct. Private-tenant source publication is FAILED in the baseline; full independent packaged SDK/provider and production UX remain incomplete.
- Checklist completion in the first three sections means specification quality is ready for review. It does not approve the specification, satisfy the open gates, or mark #3908/#3608 delivered.
