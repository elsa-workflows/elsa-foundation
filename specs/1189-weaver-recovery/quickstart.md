# Validation guide

Record actual source-bound receipts on #3441 and its PR. Use new owned output roots, unique local
versions and owned ports. Check machine load before interpreting a timing failure. Do not use
developer credentials or change a provider configuration to obtain deterministic protocol evidence.

## Component and restart behaviour

To simulate a restart, dispose a host or service provider. Then build a new one over the same record
store: in memory for unit tests, SQLite for the persistence tests. Control the deadlines with the
existing fake clock. Coordinate collection, write and receipt races with barriers, not sleeps.

Cover each of the six story 2 boundaries, before and after each deadline:
1. idle;
2. pending review;
3. Approved and not dispatched;
4. dispatched without a receipt;
5. a receipt recorded with the turn still paused;
6. a provider turn in progress.

For each boundary, assert:
- the recovered outcome;
- the number of executions at the owning fixture;
- the audit `settledBy` value;
- that claims are rebuilt and busy is settled.

Also cover:
- fence takeover and `ownership_lost`;
- an identical retry after a restart, which returns the original message;
- pause fingerprint stability;
- unreadable state;
- owner resolution, followed by a late receipt;
- the story 2.3 recheck refusals;
- list scoping across actors, tenants and workspaces with `take=1`.

```sh
dotnet test Extensions/Agent/tests/Tests/Elsa.Agent.Tests.csproj -c Release
dotnet test Extensions/Agent/tests/Persistence/Tests/Elsa.Agent.Persistence.Tests.csproj -c Release
ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1 dotnet test Extensions/Agent/tests/Persistence/ProviderTests/Elsa.Agent.Persistence.ProviderTests.csproj -c Release
python3 -m unittest discover -s tools/studio/Weaver.Conformance.Probe -p 'test_*.py'
```

Run these from `Studio/`:

```sh
pnpm --filter @elsa-workflows/studio-web test
pnpm typecheck
pnpm lint
```

Then run the architecture guard and maps:

```sh
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

## Disclosure growth

Drive a session through at least 40 checkpoints that alternate repeated and new disclosures.

Before the change, record:
- stored snapshot volume;
- owner admissions per pass.

After the change, show:
- volume is proportional to distinct snapshots;
- one admission per distinct snapshot per pass;
- re-admission after every write is preserved.

Reach both limits and check the distinct outcome and the continuation link.

## Normal composed Studio

Configure the Workbench with the EF store on SQLite. Run the complete Studio browser suite. Then run the recovery journeys:
- reload with a pending review;
- reopen the dock;
- a dropped stream showing Cancelled or Uncertain;
- a not-started prompt with Start and withdraw;
- owner resolution after a page change;
- a limit continuation;
- a second tab;
- a foreign-user list.

## Final independent package witness

Use committed source and a new absolute output directory outside the checkout. Run the complete witness: the existing original 38, the 4 current-authority cases, the 26 autonomy and lifecycle cases and the [restart inventory](contracts/recovery.md#4-mandatory-native-restart-inventory), ten cases on each actual adapter plus its in-memory negative control: 89 in all. The backend consumer configures the packaged EF store on SQLite and the driver restarts that host process itself between phases; the after-deadline phase waits out the real five-minute prepared lifetime once per adapter.

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver3441-proof \
  --version 4.0.0-weaver3441.local1
```

This evidence is deterministic provider-protocol evidence. Do not infer live-provider qualification from it.
