# Topology declaration controls

These offline Python controls validate a declared inventory for the program's future two- and four-worker fixture.
They start no host, network, process, database or cloud resource. They belong to the existing
[manual qualification fixtures](../README.md), outside the normal solution and test-project inventory.

Schema 2 requires an explicit `driverPlacement`. `DatabaseHost` describes the original five-host boundary for four
workers: a separate database VM also runs the driver. `SeparateHost` describes an optional sixth independent driver.
The latter is a different measurement boundary, not an additional acceptance requirement. Both require distinct
declared workers and a database distinct from every worker. Reusing a resource ID with different casing is rejected.

Every host declares a Linux `machineId`, Azure VM `resourceId`, RFC1918 `privateAddress`, pinned `sshHostKeySha256`,
`sshUser`, normalized absolute `taskRoot` ending in the fresh `runId`, and, for workers, a `port`. The inventory also
pins `sourceCommit`, `sourceTree` and the common rebuilt `binarySha256` cohort. The offline controls contain synthetic
declarations only; they are not a usable deployment inventory.

From the repository root, run the finite, standard-library controls with:

```sh
python3 -I specs/1183-runtime-efficiency-program/evidence/topology/test_topology.py
```

After preparing an actual inventory, its syntax can be checked with:

```sh
python3 -I specs/1183-runtime-efficiency-program/evidence/topology/topology.py /absolute/path/to/inventory.json
```

`manifestValid` and `declaredIndependentHosts` concern supplied declarations only. A live controller must independently
corroborate machine/resource identities, SSH keys, source/binary hashes, network and listener ownership. It must prove
actual actor participation and retain fault/recovery and cleanup evidence. These controls establish none of that,
nor capacity, timing, approval or engine qualification. Keep the database/driver CPU cohost explicit when comparing
two and four workers. An independent-start capacity cohort is not a clustered-actor cohort.

This source preparation does not complete [M5 or the program](../../../../docs/program-goals/runtime-efficiency.md).
New Azure spending or destructive cleanup requires the owner's explicit approval after a concrete reviewed fixture,
cost and cleanup packet. A valid manifest does not grant that approval.
