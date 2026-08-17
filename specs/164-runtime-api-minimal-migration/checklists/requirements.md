# Runtime migration checklist

- [x] Historical capture runner is committed before production migration.
- [x] Baseline has 24 route registrations and 24 OpenAPI operations.
- [x] Baseline includes all-route 401, all-route authenticated success, errors, not-found, and route/body evidence.
- [x] Production mapper has complete HTTP/OpenAPI deep comparison with reviewed two-sided approvals.
- [x] Shared authorization matrix covers 401/403/exact/implied/wildcard/normalized/tenant/resource behavior (16 cases with retained FE canary).
- [x] Three-cycle collectibility executes mapped delegates, serializers, auth/provider seams, DI, and disposal.
- [x] Runtime E2E runs against rebuilt Workbench and fresh DB (20 GET and 10 write cases).
- [ ] Architecture, maps, repository-wide formatter, full build, and diff gates are green (the changed Runtime/API/Core files pass scoped formatter verification; repository-wide formatter diagnostics remain an existing follow-up).
- [ ] Final report and follow-up issues are reviewed.
