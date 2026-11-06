# Packaged standalone Jobs witness

`../verify-packaged-jobs.py` freezes a full source SHA and publish-state SHA, computes the Jobs
package closure, packs that exact tree, and verifies archive provenance. `--stage publish` creates a
clean console application outside the checkout with exact `PackageReference` versions and a fresh
NuGet cache, restores in locked mode, and publishes it. The receipt verifies the selected archives and
deployed Jobs assemblies and retains the complete external package versions, restore diagnostics and
provider asset hashes. No package feed is published or changed.
The execution receipt also records the actual engine's `sqlite_version()` or PostgreSQL `version()`
result, rather than inferring a database version from its package or container tag.

`--stage execute` then runs `run_witness.py` against that deployed application, from its deployment
directory. It launches separate producer, inspector and worker processes using the public Jobs API.
The input is a useful report with two line items, five units and a calculated subtotal of 52.00.
The report also records attempt entry files, which provide barriers for the crash and pause controls.

```sh
python3 tools/jobs/verify-packaged-jobs.py \
  --source-ref FULL_SOURCE_SHA \
  --publish-state-ref FULL_PUBLISH_STATE_SHA \
  --evidence-dir /tmp/elsa-jobs-package-evidence \
  --provider sqlite --stage execute
```

Use `--provider postgresql` for the same executable and checks against an owned local PostgreSQL 16
container. The controller creates its own database, random credentials and container identity, then
removes the owned container. It accepts no external database connection string. SQLite uses an owned
file in the evidence directory. Both profiles initialize retained migrations once and start every
subsequent process in schema validation mode.

The focused witness checks ordinary-key replay/conflict, a deliberately discarded producer acceptance
response, unsupported-handler preservation, a useful retained typed result across fresh processes,
acceptance while paused, the durable start-permission cutoff, force-killed worker recovery, rejection
of a stale completion, and a stable invocation idempotency key across attempts. Deliberately discarding
a producer response is recorded separately from actual provider commit-ack fault injection. A stable
invocation key is not evidence that a downstream physical effect deduplicated it.

The controller signals only process sessions it created. Each command, result, log, case and process ID
is retained in the owned evidence directory. A failed or unavailable profile fails its receipt; it is
never silently counted as passed. This is one standalone slice. Full scheduling, retry, security,
native/BPMN integration and composed Studio acceptance retain their own gates.
Both scripts refuse optimized Python execution because their witness assertions must remain enabled.

The C# probe is a trusted qualification host, not a product CLI or operational API. Its JSON command
files are created by the controller and must not be exposed as a service to untrusted callers.
