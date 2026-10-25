# Validation guide

1. Read #3075 and verify its 24 native prerequisites and the current main gate.
2. Use the issue-owned clean worktree/candidate and the existing normal Workbench/Studio composition.
3. Rebuild the 11 required host/frontend groups and retain exact artifact hashes. Run affected suites, all three workflow shuffle seeds, lint/typecheck, architecture and map checks.
4. Execute each composed row with disposable private fixtures. Inspect the exact returned run/activity/evidence and publication identities. Exercise the finite laptop/narrow/theme/keyboard/failure/permission cases in the issue.
5. Root inspects every claimed capture and receipt, records actual negative/unavailable outcomes and resolves reproduced defects before acceptance.
6. Reconcile existing guidance and UX-01–18 records. Review the complete final diff independently.
7. Open one #3075 PR with evidence mapped to every acceptance criterion. Retain exact-head CI/Studio/security, actual required integration coverage, separate application approval/merge and resulting-main gates.

Private operational runners and raw evidence stay outside the repository. Public issue/PR comments carry legible acceptance and delivery records without credential/configuration values. No final row is passed by this guide.

## Required build groups

Build the nine frontend clients with `pnpm --dir <Client path> build`, and the two hosts with `dotnet build <project> -c Debug -p:RestoreLockedMode=true`. Retain the command outcome and hashes of each group's produced assets/assemblies; a group count alone does not establish provenance.

| Group | Client path or host project |
|---|---|
| App client | `Studio/src/apps/Elsa.Studio.Web/Client` |
| Dashboard client | `Studio/src/essentials/Elsa.Studio.Dashboard/Client` |
| Workflows client | `Studio/src/essentials/Elsa.Studio.Workflows/Client` |
| Secrets client | `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client` |
| Connections client | `Extensions/Connections/studio/Elsa.Studio.Connections/Client` |
| Feature Management client | `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client` |
| Workflow Dashboard client | `Extensions/Workflows.Dashboard/studio/Elsa.Studio.Workflows.Dashboard/Client` |
| Structured Logs client | `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client` |
| OpenTelemetry client | `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client` |
| Studio host | `Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj` |
| Workbench host | `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` |
