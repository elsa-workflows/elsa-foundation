# Manual qualification fixtures

These are bounded, one-off qualification sources for Runtime Efficiency #2531, outside the normal solution and CI
project inventory. Their project and lock files are frozen as templates. From the qualified source checkout, prepare
only the desired fixture before using its README's build/run commands, for example:

```sh
python3 specs/1183-runtime-efficiency-program/evidence/prepare-projects.py native-qualification retained-boundaries
```

The seven accepted names are `native-qualification`, `retained-boundaries`, `timer-qualification`, `etl-qualification`,
`bpmn-qualification` `diagnostics-attribution` and the unfinished `cluster-control` prototype. The helper copies the exact project/lock bytes into ignored local
files and refuses to replace differing local files. It never restores, builds, starts a server or runs a workload.
Keep the current checkout's relative source paths intact; source-archive qualification uses the same explicit
materialization before building. Projects built for earlier receipts had the same project/lock bytes at their
ordinary filenames; this packaging change does not change their production libraries or timing results.

Read each fixture's bounds and measurement/accounting boundary before use. Use fresh task-owned output directories,
private synthetic data/connections and a recorded source/binary/configuration identity. Preserve failures and account
for every attempted execution. None of these fixtures is a benchmark service, CI timing gate or production SLO.
