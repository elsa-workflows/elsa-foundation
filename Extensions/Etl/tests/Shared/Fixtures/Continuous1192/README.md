# Spec 1192 continuous-run fixture

`runtime.db` is the ETL runtime database written by the spec 1192 writer (source `6226b78af`, migration `20261102170000_ConnectorRunCompletion`).

It holds one continuous HTTP run, `compaction-1192`, in scope `tenant-fixture/compaction-1192`. Its two shards both idle with their journals cleared and no lease:
- **Shard `a`:** 3 accepted pages, 3 batches and 4 records; its current request is sequence 3.
- **Shard `b`:** 2 accepted pages, 2 batches and 2 records; its current request is sequence 2.

**How it was captured.** `EtlContinuousFixtureCaptureTests` ran with `ETL_1192_FIXTURE_CAPTURE_PATH` set, against a loopback feed on port 29196, because the pinned HTTP binding handles hash the endpoint. The shards hold no attached page, so no page-cache files are needed.

**Who reads it.** Spec 1196's migration test migrates a copy, checks the prior rows and foreign keys, resumes the run on the same port, and checks that the shards' history below their windows is compacted.

SHA-256 of `runtime.db`: `7cb52ed573db7fffb21ffa803c57d27a13a2974c4bddb2e002275cce461b4f14`.
