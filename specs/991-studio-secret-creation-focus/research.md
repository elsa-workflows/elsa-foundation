# Research — Studio secret-creation focus

## Inputs and source baseline

- Product requirements: approved [specification](spec.md) for Foundation UX issue [#3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066), mapped to its three priority-ordered user stories and functional requirements.
- Planning branch: `agent/codex/3066-secret-creation`, at `d628fc0da1e44ce5ea61768fbf6377b43919bacb` after the worktree fast-forward.
- Source provenance: the spec records main at `04ab513208cee2934dbe3765cae63d2b498aa5cb` and the reviewed source anchor `4c933f768f4cb11b16249d0f5a971312cd293f56`. Root confirmed the Secrets and Studio source relevant to this design is byte-identical from that main base through `d628fc0da1e44ce5ea61768fbf6377b43919bacb`.
- The canonical code graph returned no Studio TSX symbols for this area, so exact known source paths were used as the fallback for source review. No worktree indexing was performed.

## Decisions

### Keep the existing creation contract and state

**Decision**: Change presentation and disclosure only. Preserve the current create payload, field omission, defaults, eligibility, Store and Type behavior, Scope behavior, expiry conversion, error retention, cancellation, focus, and masking.

**Rationale**: The approved issue is about everyday creation flow and progressive disclosure. Current source behavior is the authoritative contract. A changed dialog order must not silently change what is saved or when creation is allowed.

**Alternatives considered**: Introducing a name suggestion/normalization rule, new validation, or altered Store eligibility. Rejected because none is needed to improve form order and each would expand semantics beyond the approved scope.

### Reuse shared More options disclosure

**Decision**: Reuse `AdvancedSection` from Studio UI without changing that shared primitive. Keep optional Display name, Description, Tags, Store, and Scope in it. Keep an empty Store selector directly reachable under existing behavior.

**Rationale**: The shared disclosure already provides closed-by-default presentation, a generic set count, configured manual collapse, mounted content, and forced recovery opening for errors. Reuse keeps the UX consistent and preserves authored values. The empty-Store case is an existing recovery path and must not be hidden by the normal collapse.

**Alternatives considered**: A Secrets-only disclosure or a shared primitive change. Rejected because the existing component covers the required behavior; a new primitive or shared behavior change is unnecessary.

### Keep Store context and Scope meaning outside the fold

**Decision**: Keep compact selected-Store context and the existing canonical Scope matching/access hint visible outside More options, with the hint rendered once and still associated accessibly with the Scope input.

**Rationale**: Users need to understand which Store is selected and what Scope means without opening the optional section. The current hint explains matching semantics and explicitly says Scope does not grant access; no new guarantee or policy should be inferred.

**Alternatives considered**: Moving all Store/Scope context behind More options or duplicating the hint inside and outside. Rejected because the former removes useful context and the latter duplicates content and can create conflicting accessible descriptions. A bounded module-internal picker association is acceptable if needed to avoid the duplicate while preserving default behavior for other callers.

### Keep counts generic and secret-safe

**Decision**: Count authored optional metadata and a Store changed from its existing default. Exclude the default Store, Technical name, Value, Configuration key, and Expires. Do not put values or source text in a badge or summary.

**Rationale**: A count communicates that optional choices were configured without disclosing names, tags, scope values, or secret material. The internally selected default Store is not user-authored configuration.

**Alternatives considered**: Counting all populated controls or using field contents as summary text. Rejected because defaults would look authored and content could disclose sensitive information.

### Keep Scope failure advisory and recoverable

**Decision**: Preserve the current catalog-failure behavior: keep creation eligibility unchanged, open More options for Retry, preserve the exact custom Scope and focus, force the disclosure open while the error is active, and preserve the shared latch after recovery until the user collapses it.

**Rationale**: Scope suggestions assist selection; they do not determine whether a custom Scope or otherwise-valid Secret can be submitted. The shared disclosure already exposes the Retry path on error.

**Alternatives considered**: Blocking creation, clearing custom Scope, or making the disclosure permanently forced open. Rejected because those change established eligibility or discard user work.

### Add no stored-value read or response exposure

**Decision**: Keep the dialog in the new-Secret flow with metadata-only fixture responses. Do not add an existing-Secret selector, stored-value fetch, or response that echoes entered Value or Configuration key.

**Rationale**: Existing page reads are metadata, and the dialog sends the source entered for the new Secret through the current create request. Fetching or returning stored material is outside this UI change and violates the source confidentiality boundary.

**Alternatives considered**: Reading back the created/stored value to populate a summary or fixture response. Rejected because creation metadata is sufficient and raw source material must not be exposed there.

### Keep package and map changes bounded

**Decision**: The current manifest declares SDK but not Studio UI. Declare `@elsa-workflows/studio-ui` directly for the new import, even if aliases or hoisting could resolve it; change only its importer in `Studio/pnpm-lock.yaml`. Do not edit `AdvancedSection`. Documentation maps and findings are generated/published by CI to `generated-maps`, not committed. The local map check only verifies the committed dependency map.

**Rationale**: The dependency should follow the existing consumer boundary. Current generated-map policy avoids staging ignored documentation maps; it does not affect this UI-only dependency decision.

**Alternatives considered**: Copying disclosure behavior into the Secrets module, changing a shared package, or committing generated docs-map output. Rejected because these add unnecessary duplication or violate current map ownership policy.

## Source observations

The reviewed paths are:

- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretDialogs.tsx`
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/SecretScopePicker.tsx`
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/__tests__/secretDialogs.test.tsx`
- `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/package.json` and `vite.config.ts`
- `Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/disclosure/AdvancedSection.tsx`
- `Studio/tests/browser/secret-scopes.spec.ts` and `secretScopesFixture.tsx`

The current form places the required Value or Configuration key after optional controls and descriptor copy. The dialog uses a scrollable body with fixed actions. The actual-host-styles Playwright fixture is the proof surface for viewport visibility; source order or a component-only test cannot establish that the entire control fits in the viewport.

Existing behavior to preserve includes:

- Technical name normalization and validation; Display name default/fallback; trimmed Description omission; current Tags parsing.
- Value and Configuration key selection based on Store type; Configuration key requiredness/trim behavior; parent-held source retention during Type/Store changes; Value remount returning to masked state.
- Existing Store defaults and eligibility, including a Type change that can leave Store empty and the direct Type/Store recovery controls.
- Exact custom Scope text and current matching policy; advisory catalog failure and Retry.
- Current Expires request conversion, create errors, Cancel/Escape behavior, focus restoration, and scroll behavior.
- The current create flow has no existing-Secret selector or stored-value read; fixture responses remain metadata-only.

`AdvancedSection` already supports the normal collapsed state, authored-value counting, manual collapse when configured, and automatic open-on-error with user dismissal. Reuse it unchanged. The Scope picker currently renders the canonical matching/access hint with its `aria-describedby` relationship; if the caller renders that hint outside the disclosure, a bounded internal association may suppress the duplicate while retaining local option and error descriptions and the default behavior for other callers.

The current tall-error browser case scrolls to Expires. Since Expires moves to the primary region, update that case to scroll to an actual below-fold optional field or long help. Retain its body-scroll, fixed-action, error, focus, and authored-value checks.

## No unresolved research questions

No product-policy ambiguity remains in the approved specification. The new Studio UI import requires a direct dependency declaration in the consuming Secrets package; there is no remaining package-policy question. Browser, test, build, map, and hosted results are future verification, not research claims, and none was run for these plan artifacts.
