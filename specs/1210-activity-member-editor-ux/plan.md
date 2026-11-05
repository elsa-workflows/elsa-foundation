# Plan

Reuse studio-ui tab keyboard behavior and IDs. Keep all member panels mounted and hide inactive panels. Use existing token-based control styles, align grid rows at their start and size inputs consistently. Category uses a native editable datalist populated from contract categories; do not invent a remote category taxonomy.

Adapt diagnostic focus to select the containing member panel before focusing. Keep exact type/storage/durability targets. Preserve contract mutation and breaking-change review paths.

Constitution review: frontend-only internal implementation, no new public extension contract, dependencies, domain ownership, persistence policy or draft architecture. ADR 0080 risk-based testing applies. Review spec before implementation: accepted as the bounded expression of the owner's screenshot, no unresolved architecture decision.

Validation: affected Workflows tests/typecheck, focused isolated browser suite, composed feature journey in four viewport/theme projects. Review current-commit screenshots independently and retain downloadable CI artifacts. Shared machine builds use normal queue; high load is recorded when interpreting timing failures.
