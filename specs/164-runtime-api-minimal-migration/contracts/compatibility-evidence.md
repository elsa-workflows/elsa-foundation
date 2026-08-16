# Compatibility evidence contract

The historical runner must execute against a detached FastEndpoints source worktree and write a receipt containing the source commit, runner commit, registration count, case count, operation count, and SHA-256 hashes. Captured HTTP evidence must include anonymous challenges for every route, authenticated success for every route family, actual route/query/body binding, malformed/empty/literal-null/absent-content-type behavior, not-found behavior, and alteration status dispositions.

After migration, compatibility tests consume the frozen fixture rather than regenerating expected values from the Minimal API implementation. Any deliberate OpenAPI or HTTP facet difference is recorded with before and after values and an explicit approval in the migration report.
