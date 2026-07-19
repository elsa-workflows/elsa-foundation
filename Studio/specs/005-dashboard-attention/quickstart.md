# Quickstart: Dashboard Widgets and Attention

## Repositories and branches

- Studio: `/Users/sipke/.codex/worktrees/d246/elsa-foundation-studio`
- Foundation: `/Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation`
- Branch in both: `codex/dashboard-attention`

## Build prerequisites

```bash
cd /Users/sipke/.codex/worktrees/dashboard-attention/elsa-foundation
dotnet restore Elsa.Server.slnx

cd /Users/sipke/.codex/worktrees/d246/elsa-foundation-studio
pnpm install --frozen-lockfile
```

## Focused backend validation

```bash
dotnet test tests/Elsa/Attention/Core/Tests/Elsa.Attention.Core.Tests.csproj
dotnet test tests/Elsa/Attention/Api/Tests/Elsa.Attention.Api.Tests.csproj
dotnet test tests/Elsa/Studio/Preferences/Tests/Elsa.Studio.Preferences.Tests.csproj
dotnet test tests/Elsa/Workflows/Dashboard/Tests/Elsa.Workflows.Dashboard.Tests.csproj
```

Prove:

- partial contributor success, auth/discovery, bounds, timeouts, cancellation;
- preference scope/revision/quota/migration and missing identity failure;
- complete aggregates beyond normal page size;
- live-published, invalid-draft, DST, range, test-run, incident/outcome semantics;
- identical official-provider fixtures.

## Focused Studio validation

```bash
pnpm --filter @elsa-workflows/studio-dashboard typecheck
pnpm --filter @elsa-workflows/studio-dashboard test
pnpm --filter @elsa-workflows/studio-attention test
pnpm --filter @elsa-workflows/studio-workflows-dashboard test
pnpm --filter @elsa-workflows/studio-web test
dotnet test tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj
```

## Full validation

Foundation:

```bash
dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet test Elsa.Server.test.slnf -c Release --no-build -p:WarningsNotAsErrors=NU1603
```

Studio:

```bash
pnpm typecheck
pnpm lint
pnpm test
pnpm build
dotnet test tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj
```

## Real-screen proof

Run Elsa Server with Attention, Studio Preferences, Workflow Dashboard, and the
three initial contributors enabled. Run Studio against it and open `/dashboard`.

Verify:

1. Exactly Attention, Workflow Portfolio, and Workflow Run Health appear by
   default; the old sample cards do not exist.
2. Keyboard controls reorder, resize, hide, and restore widgets; preferences
   survive reload and a second authenticated session.
3. Global/per-widget refresh, effective intervals, cancellation, timeout, retry,
   last-updated, and next-refresh states behave independently.
4. Tenant/host switching never flashes prior-scope data.
5. Attention groups correlations, filters All/Critical/Snoozed, persists snoozes,
   breaks snooze on escalation/generation, isolates contributor failure, and
   shows all-clear when empty.
6. Workflow counts match seeded datasets larger than one page; run buckets match
   the selected IANA time zone over a daylight-saving boundary.
7. Small, medium, wide, and full layouts remain keyboard accessible and usable.
