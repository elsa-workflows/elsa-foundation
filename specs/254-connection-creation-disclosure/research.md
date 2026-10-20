# Research: Connection Creation Disclosure

**Status**: Source-grounded planning notes; implementation and runtime behavior have not been tested.

## Findings and decisions

1. **Keep the specialization narrow.** The dialog is descriptor-driven. Apply the improved path only when the selected type is exactly the built-in HTTP type and the selected scheme is exactly its API-key scheme. Unknown types, schemes, and contributed fields keep generic rendering and descriptor validation; do not infer field semantics from labels or matching field names.

2. **Honor the HTTP address contract when metadata can express it.** The built-in HTTP descriptor includes Base URL and Allowed hosts. The existing backend contract accepts either nonblank address. The client gate and primary-path guidance can therefore apply when both address controls are present. If either required metadata field is missing, leave generic validation and the backend error path intact rather than synthesizing a field or disabling submission impossibly. The same HTTP address rule applies with other HTTP schemes.

3. **Treat placement as a verified runtime contract, not descriptor enum metadata.** The built-in descriptor exposes a plain placement field without an advertised option list. Runtime behavior recognizes Header and Query case-insensitively, defaults a missing or whitespace-only value to Header, and does not trim a nonblank value. Render a selection control for those two supported values, emit canonical lowercase selections, and reject unsupported values rather than coercing them. Do not change descriptor or API metadata.

4. **Preserve optional authored values while reducing noise.** Keep Base URL direct and group only known optional HTTP/API-key settings under More options. Show only the effective placement’s optional name field, while retaining both names in form state. Required fields override relevance-based grouping and stay visible exactly once. Use Authorization and api_key only as helper or placeholder text; do not prefill either. An explicit operator entry equal to a runtime default remains authored. The stock insecure-HTTP toggle stays unchecked and must omit its setting when unchecked, including after an on→off interaction.

5. **Make the advanced error recovery behavior safe.** The existing public AdvancedSection export is available from @elsa-workflows/studio-ui. Its current forced-open behavior can collapse when the error clears, and its preference hook persists user changes. The planning candidate is a private local state refinement in AdvancedSection that remembers an automatic reveal through recovery until manual collapse, without a new public prop or writing an automatic reveal to localStorage. Keep children mounted so control identity, values, and focus survive correction. Root approved this bounded private design on 2026-10-20; capture the failing regression first.

6. **Keep the Secret boundary unchanged.** Reuse the existing credential control and metadata-only Secret lookup. It already represents Existing Secret versus New value and handles eligibility, permission denial, unavailable results, retry, cancellation, endpoint changes, and create failures. No Secret values, endpoint, authorization, policy, or persistence changes are in scope.

7. **Use existing package and browser seams.** The Connections client has a Vitest suite and Vite build. Importing AdvancedSection requires the internal @elsa-workflows/studio-ui workspace dependency and the corresponding Studio lockfile update; Vite already externalizes this package. The real browser fixture is Studio/tests/browser/connections-management.spec.ts with connectionsFixture.tsx; add the create-flow coverage there rather than creating a new harness.

## Source pointers to recheck before implementation

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

Root approved the private AdvancedSection state refinement on 2026-10-20 after reading the current component/state hook and the issue criteria. The failing regression must establish the desired error→correction→recovery→manual-collapse sequence, preserve the child/value/focus identity, and prove automatic reveal did not change the persisted user preference. No implementation or tests have been run as part of this preparation.
