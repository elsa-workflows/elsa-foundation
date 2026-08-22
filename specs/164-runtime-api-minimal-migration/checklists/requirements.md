# Runtime migration checklist

- [x] Historical capture runner is committed before production migration.
- [x] Baseline has 24 route registrations and 24 OpenAPI operations.
- [x] Baseline includes all-route 401, all-route authenticated success, errors, not-found, and route/body evidence.
- [x] Production mapper has complete HTTP/OpenAPI deep comparison with reviewed two-sided approvals.
- [x] Shared authorization matrix covers 401/403/exact/implied/wildcard/normalized/tenant/resource behavior (16 cases with retained FE canary).
- [x] Three-cycle collectibility executes the full mapped application pipeline (routing, authentication, authorization/resource evaluation, typed response, body binding, source-generated JSON, native OpenAPI, disposal, and unload) in every cycle.
- [x] Final post-W6 Runtime E2E evidence exists against rebuilt Workbench and fresh DB (20 GET and 10 write cases).
- [ ] Final Architecture, maps, repository-wide formatter, full build, and diff gates are green (focused owner gates, maps, affected builds, and scoped Runtime/API/Core formatter pass; repository-wide formatter has unrelated baseline diagnostics and is not claimed green).
- [x] Final report and follow-up issues are reviewed.
