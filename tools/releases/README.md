# Workflow release qualification tools

The transport consumer verifies the locally packed release codec and its package boundary. It is not a workflow release,
owner-semantic closure, target integration or promotion witness.

```sh
python3 tools/releases/run_transport_consumer.py --output /tmp/elsa-release-transport-unique
```

Choose a new output directory. The script builds/packs only the two transport projects, restores a clean consumer from the
local package feed with an isolated cache and public producer dependency feeds, exercises the real DI/ZIP codec and records package hashes,
source commit/dirty state and the consumer dependency graph. It retains its owned output and never starts or stops a host.
It verifies the restored archives and executed DLLs against the exact packed bytes and allows only the two transport Elsa
packages in the consumer graph. Final evidence must identify a committed source; an exploratory dirty run is recorded as such.

The complete native/BPMN/ETL two-target and process/Studio qualification remains in
[spec 1225](../../specs/1225-workflow-release-promotion/quickstart.md).

The installed package witness also resolves the semantic framework and proves an unregistered owner codec remains
Unsupported. This is a fail-closed framework/package boundary control; it does not qualify any native, BPMN or ETL owner adapter.

The Runtime bridge has a separate installed-package witness:

```sh
python3 tools/releases/run_runtime_consumer.py --output /tmp/elsa-release-runtime-unique
```

It packs the concrete Runtime-only project closure, restores exact local archives into an isolated cache, and verifies
the original template hash vector, lossless template codec, copied payloads, byte limit and template-owner hash verdict.
Every resolved Elsa package and executed assembly must match that feed. The consumer retains mandatory module sites
and grants no independent-start role; its deliberately refusing native hasher ensures it cannot claim native execution
or native hash qualification. The whole Runtime release test project separately exercises the actual Runtime native hasher.
