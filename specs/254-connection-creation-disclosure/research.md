# Research: Connection Creation Disclosure

**Status**: Root-reviewed design. Local execution evidence and limits are recorded in tasks.md; round-one review and hosted CI remain pending.

## Findings and decisions

1. **Keep the specialization narrow.** The dialog is descriptor-driven. Apply the improved path only when the selected type is exactly the built-in HTTP type and the selected scheme is exactly its API-key scheme. Unknown types, schemes, and contributed fields keep generic rendering and descriptor validation; do not infer field semantics from labels or matching field names.

2. **Honor the HTTP address contract without contradicting required metadata.** The built-in HTTP descriptor includes Base URL and Allowed hosts as optional fields. Apply one-of validation when both recognized address fields are present; offer guidance only when both are optional, using the copy “Enter the Base URL above or configure Allowed hosts in More options.” If either address field is descriptor-required, keep its direct control and mandatory validation and do not offer either/or guidance. If fields are missing or metadata is unfamiliar, retain generic validation and the backend error path. Apply the same address rule with other HTTP schemes too.

3. **Treat placement as a verified runtime contract, not descriptor enum metadata.** The built-in descriptor exposes a plain placement field without an advertised option list. Runtime behavior recognizes Header and Query case-insensitively, defaults a missing or whitespace-only value to Header, and does not trim a nonblank value. Render a selection control for those two supported values, emit canonical lowercase selections, and reject unsupported values rather than coercing them. Do not change descriptor or API metadata.

4. **Preserve optional authored values while reducing noise.** Put the required API-key credential before Base URL; keep Base URL direct and group only known optional settings under More options. Keep existing Basic Username→Password and custom-scheme ordering. Show only the effective placement’s optional name field while retaining both names in form state. Descriptor-required address and other fields remain direct, mandatory, and visible exactly once. Use Authorization and api_key only as helper or placeholder text; do not prefill either. An explicit operator entry equal to a runtime default remains authored. The stock insecure-HTTP toggle stays unchecked and must omit its setting when unchecked, including after an on→off interaction.

5. **Make the advanced error recovery behavior safe.** The existing public AdvancedSection export is available from @elsa-workflows/studio-ui. Its original forced-open behavior could collapse when the error cleared, and its preference hook persists user changes. Root approved a private local state refinement in AdvancedSection that remembers an automatic reveal through recovery until manual collapse, without a new public prop or writing an automatic reveal to localStorage. Keep children mounted so control identity, values, and focus survive correction. The regression and implementation are included in the local green snapshot recorded in tasks.md.

6. **Keep the Secret boundary unchanged.** Reuse the existing credential control and metadata-only Secret lookup. It already represents Existing Secret versus New value and handles eligibility, permission denial, unavailable results, retry, cancellation, endpoint changes, and create failures. No Secret values, endpoint, authorization, policy, or persistence changes are in scope.

7. **Use existing package and browser seams.** The Connections client has a Vitest suite and Vite build. Importing AdvancedSection requires the internal @elsa-workflows/studio-ui workspace dependency and the corresponding Studio lockfile update; Vite already externalizes this package. The real browser fixture is Studio/tests/browser/connections-management.spec.ts with connectionsFixture.tsx; add the create-flow coverage there rather than creating a new harness.

## Relevant source pointers

- Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionCreateDialog.tsx — type/scheme selection, validation, form order, request construction.
- Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/ConnectionCredentialField.tsx — Secret reference and New value behavior; preserve unchanged.
- Essentials/src/Connections/Elsa.Connections/Http/HttpConnectionTypeProvider.cs — built-in descriptor fields and required metadata.
- Essentials/src/Connections/Core/Models/ConnectionConstants.cs and Essentials/src/Connections/Elsa.Connections/Http/HttpRequestAuthenticators.cs — placement values and runtime default semantics.
- Essentials/src/Connections/Elsa.Connections/Services/DefaultConnectionManager.cs — server-side Base URL / Allowed hosts one-of validation.
- Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx and DisclosureFold.tsx — current error forcing, disclosure state, child mounting, and focus recovery contract.
- Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/__tests__/disclosure.test.tsx — shared disclosure regression seam.
- Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/__tests__/connectionCreateDialog.test.tsx — existing form and Secret regression seam.
- Studio/tests/browser/connections-management.spec.ts and connectionsFixture.tsx — actual Connections page browser harness.

## Root design review

Root approved the private AdvancedSection state refinement on 2026-10-20 after reading the component/state hook and issue criteria. The regression establishes error→correction→recovery→manual-collapse, preserving child/value/focus identity and the persisted user preference. The initial red checkpoint and later green local snapshot are recorded separately in tasks.md. Round-one independent review and hosted CI remain pending.
