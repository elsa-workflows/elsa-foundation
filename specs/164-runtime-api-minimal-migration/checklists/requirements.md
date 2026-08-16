# Runtime migration checklist

- [x] Historical capture runner is committed before production migration.
- [x] Baseline has 24 route registrations and 24 OpenAPI operations.
- [x] Baseline includes all-route 401, all-route authenticated success, errors, not-found, and route/body evidence.
- [x] Production mapper has complete HTTP/OpenAPI deep comparison with reviewed two-sided approvals.
- [ ] Shared authorization matrix covers 401/403/exact/implied/wildcard/normalized/tenant/resource behavior.
- [ ] Three-cycle collectibility executes mapped delegates, serializers, auth/provider seams, DI, and disposal.
- [ ] Runtime E2E runs against rebuilt Workbench and fresh DB.
- [ ] Architecture, maps, formatter, full build, and diff gates are green.
- [ ] Final report and follow-up issues are reviewed.
