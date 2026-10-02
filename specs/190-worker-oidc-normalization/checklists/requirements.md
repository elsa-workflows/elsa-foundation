# Specification Quality Checklist: Worker external bearer normalization

**Purpose**: Review completeness before implementation planning.
**Created**: 2026-10-02
**Feature**: [spec.md](../spec.md)

## Content quality

- [x] Actor outcomes precede implementation choices; exact public contracts remain in the plan/contracts.
- [x] Primary journeys cover secured execution, explicit composition and operational rule changes.
- [x] Mandatory sections and bounded assumptions are present.

## Requirement completeness

- [x] No unresolved product decision changes the fixed-provider/tenant first layout.
- [x] Requirements have observable acceptance scenarios and measurable success criteria.
- [x] Invalid configuration, invalid authentication, denied permission, backend failure and cancellation are distinct.
- [x] Default compatibility, namespace ownership and bearer-only setup are explicit.
- [x] Durable rule changes, restart, account-write absence and real HTTP/database proof are required.
- [x] Dynamic tenancy, external interoperability and profile publication have separate revisit triggers.

## Feature readiness

- [x] Source review connects the existing no-go to a concrete adapter outcome rather than a new IAM system.
- [x] Every requirement has an acceptance owner in the forthcoming proof matrix.
- [x] Specification is ready for planning; no production implementation or host result is claimed.

## Workflow record

The installed `before_specify` Git feature hook created `codex/2304-worker-oidc-normalization-spec` from fetched main0bb. Sequential directory discovery selected190 independently of the branch. The active spec template resolved to `.specify/templates/spec-template.md`; The tracked `.specify/feature.json` and managed AGENTS plan reference move together to the new spec; they are not personal preference files. Enabled optional Git commit hooks will be satisfied by the reviewed local authoring commit. Disabled agent-context extension hooks are skipped; the plan workflow's managed AGENTS plan reference is updated separately.

Root review identified an additional framework event bypass beyond the token-validated callback: successful MessageReceived or AuthenticationFailed results can bypass real JWT validation. The plan must guard all success-producing paths and verify final options ownership before enrolling a trusted authentication type.

## Integrated authoring review

A bounded GPT-6 Luna Extra High source/artifact audit found the central contract coherent and identified two precision fixes: repeated opt-in registration was undefined, and the framework source reference used10.0.0 instead of the repository-pinned10.0.10. Root chose explicit duplicate-registration refusal with a focused acceptance control and verified the official10.0.10 handler/events source before updating citations. Earlier feedback also tightened owned Challenge/Forbidden responses, separated the raw token identity type from the distinct normalized type, and distinguished the guarded IAM replacement contract from the unmarked normalizer seam. Root rejected an unsupported initial claim that the raw identity type necessarily equals the scheme name; the corrected evidence does not make that claim.

Root reviewed actual shell middleware and the existing hosted-startup/IShellInitializer validation pattern; the plan now requires shell-owned handler/store resolution and actual activation gating rather than relying on root-only ValidateOnStart. Local Markdown links resolve, FR001-FR015 and SC001-SC006 map to the proof matrix, and all28 tasks are unique, ordered and unchecked. The first matrix-coverage script rejected shorthand identifiers; the table now spells each identifier in full, and the rerun passed. No implementation test or local-issuer/database journey has executed in this authoring unit.

Initial generated-map freshness failed on the new spec/count snapshot. Authorized regeneration changed only Spec Status Map, manifest and the Maps v1 spec count; root reviewed those findings and explicitly stages all three. The subsequent freshness check passed. Exact publication checks and resulting-main gates remain PR/issue evidence and are not claimed here before execution.

Root review after opening the PR tightened prior-callback failure handling: the stock handler logs thrown processing exceptions before AuthenticationFailed. The contract now catches non-cancellation callback exceptions and sanitizes prior Fail text inside the owned guard, while preserving refusal/NoResult semantics. The proof matrix/tasks include callback canaries; arbitrary callback-authored logging remains outside the adapter guarantee. This is an authoring correction, not an executed behavior pass.

Copilot review5388603460 on54a identified a real omitted EF ambient-scope prerequisite and two ratified unit/visibility gates. Root verified each source finding, selected host-owned fixed ordinary scope initialized before store resolution, added activation/request agreement checks without rebinding, and allocated matching/mismatched real actor controls. Every new logic class now requires independent direct-construction/stubbed branch coverage; the touched feature must be public non-sealed with virtual registration and direct service-resolution proof. These remain implementation allocations, not executed tests.

Copilot summary review5388647153 on8a raised three concerns without inline comments. The tenant binding omission is already corrected in7201; root explicitly reviewed and retained the existing Runtime.Core contract dependency under framework §2.1 instead of duplicating or relocating scope ownership. The subsequent authoring correction makes both existing named-options configurators public sealed with separate direct branch tests and narrows activation refusal to custom Challenge/Forbidden delegates. Permitted extraction/validation/failure callbacks remain trusted host code; their arbitrary response side effects are outside the adapter guarantee. The specification does not promise activation can detect such side effects. No implementation test result is implied.
