# Plan

1. Use an image-only derivative of approved M2, produced with the built-in imagegen tool. Encode an optimized local JPEG and document its provenance with existing artwork notes.
2. Extend the existing Identity-owned artwork, layout and palette allow-lists with `parcel-room`. Set the coordinated options defaults to Parcel Room; retain explicit host choices and the existing form/renderer/security seams. Use white surfaces, a small logo, a contained illustration, no story overlay and a form-first responsive layout.
3. Extend the existing contract tests for the new default, independently configured choices, prefix-correct asset serving and genuine cookie sign-in. Preserve the rest of the Identity suite. Verify a failing baseline before implementation.
4. Inspect actual source-rendered presentation at desktop, tablet and narrow widths, dark OS preference, high contrast, missing art and error feedback. Run the affected Identity project and repository architecture/maps gates. Review the bounded diff, document the result and open the scoped PR.

## Constitution check

Framework v6.1.0 and Elsa v6.3.0: the existing Layer-3 Identity implementation owns the presentation and static assets. No new project, package, service registration, public type/member, authentication contract, permission, persistence schema or cross-domain dependency. No provisional architecture rule is used. Existing tests for valid behaviors remain applicable; tests of the old default illustration are updated to the approved new default. No extension-point catalog change is needed. Recheck these constraints against the final diff.

Risks are configuration independence, anonymous embedded-resource delivery, contrast and responsive presentation. Existing HTTP contract tests and rendered browser inspection prove these at their narrowest useful levels; this cosmetic update adds no runtime workflow E2E requirement.
