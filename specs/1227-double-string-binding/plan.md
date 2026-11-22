# Implementation plan

Extend the existing binding-owned, publication-pinned conversion operation set. Put eligibility in Runtime Core alongside numeric widening and use it in both the publication resolver and runtime interpreter. Preserve the version-one operation names and fingerprints. Format finite IEEE 754 Double values using invariant round-trip text.

For the Inspector use server-resolved contracts for the current selected source, target and conversion mode. Follow the existing Activity Data Selection snapshot/revision rules. Reuse publication diagnostics for known rejection. Follow ADR 0046's scoped decision process and document this rule.

Qualify with focused characterization tests, full affected suites and a disposable composed Studio publishing/runtime journey. Use the selected-source card as the visual reference. Risk-based coverage concentrates on culture, invalid payload, protection, strict mode and stale previews.
