# Portable completion of the existing round-6 C1 evidence

This is evidence packaging for the correctness report at reviewed head `d1449c0bff679212f8a64766a95b1c29cfadbcea`, base `d481d6a5e624e35bba9fbf32033ed02aff64cbef`. It is **not a new review round, implementation change or new qualification run**. The original normal-host run executed source9c products whose backend is identical to d144.

`allowlist.txt` is an explicit list of the only files to transfer from the parent directory. `manifest.json` records their hashes and roles; `packaging-verification.json` records validation. Private `timer-host*` directories, Identity/elsa.db stores, cookie files and configuration containing login values are excluded. `round6-correctness-portable.tar.gz` contains exactly that allowlist and no directory traversal or symlink entries.

## Original evidence, unchanged

- The unchanged original `report.md` identifies C1, exact inputs/identities, source/product boundaries, scopes and known holds.
- `probe_timer_admission.py` and `resume_timer_probe.py` are **the original executed controller scripts**. Their original bytes and hashes are retained; their historical absolute paths are provenance, not portable commands. `original-script-hashes.json` verifies them.
- `timer-admission-result.json` and `timer-after-expiry-result.json` retain the original submitted HTTP request bodies/results, published material/source/incarnation, execution/input/queue identities, process identities and product hashes. `controller.log` and `expiry-controller.log` are the corresponding original controller outputs.
- The three actual closed stores are `before-restart-runtime.sqlite.gz`, `final-runtime.sqlite.gz` and `after-expiry-runtime.sqlite.gz`. They contain the original accepted input and PT1H control. Associated raw read-only model/queue JSON and verification preserve the first claim and naturally reclaimed second claim. No original state has been rewritten.
- `first-host.log`, `restarted-host.log` and `post-expiry-host.log` are all three raw qualified-run host logs. Logs and actual stores retain their original statuses and source identities.
- `diagnostic-history.json`, both `attempt1-host.log`/`attempt2-host.log` and `diagnostics/attempt*-runtime.sqlite.gz` retain **both failed setup/observer attempts**, excluded from qualification. `diagnostics/attempts.json` describes their provenance and capture limits. Their only archives are Runtime databases; private Identity/configuration directories are excluded. The first two attempts did not retain standalone original controller tracebacks; no reconstructed log is presented as raw evidence.
- Source/test review manifests/diffs and the final frozen supplied discussion snapshot remain included as report inputs. None is a new behavioral pass.

## Explicit-products adapter

`reproduce_explicit_products.py` is a **separate newly written portable adapter**, not a replacement or silently edited original. It accepts paths rather than depending on the earlier machine's checkout or product locations. `case-inputs.json` contains the exact two original publication request bodies and request IDs. Fresh publication responses supply their new source references; fresh test-account identities can produce different execution IDs.

Build Workbench and `tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj` from the pinned reviewed source, or another commit with byte-equivalent backend source, using that checkout's normal required build-slot tooling. The adapter does not build, restore, fetch, replace or copy a product. Pass the actual built DLLs and a fresh checkout location explicitly:

```sh
python3 reproduce_explicit_products.py \
  --repository /absolute/path/to/fresh/elsa-foundation \
  --workbench /absolute/path/to/products/Elsa.Workbench.dll \
  --reader /absolute/path/to/products/DeliveryRecoveryRead.dll \
  --output /absolute/path/to/new-empty-evidence-directory
```

The output path must not exist. The adapter obtains actual host/writer versions and hashes through the supplied reader, checks that their full product source commit has no backend diff from d144, and records the reader hash. It copies only repository host configuration into a fresh private content root, uses loopback ephemeral ports and ordinary demo authentication/publication/start/inspection/cancel APIs. No user host, fixed port or browser tab is used.

It stops only the exact subprocess it created. The reader's existing `--backup-sqlite` command makes fresh coherent Runtime snapshots. All SQL issued by the Python observer is `SELECT` or `PRAGMA integrity_check` against a stopped immutable read-only snapshot. It never updates an execution, queue, receipt, claim, authority or clock. It waits for **real claim expiry**, bounded by `--expiry-limit-seconds` (default180), then preserves the original queue item's higher claim token. It records the PT1H control and Pending/404 overflow observations across three processes, raw reader stdout/stderr, logs, requests, Runtime stores and product hashes. Failed setup/reproduction attempts are recorded as diagnostic; the adapter never retries a failed journey under a passing label. Its private host/Identity directory is removed after owned processes stop, and a generated explicit file manifest covers the resulting evidence files.

Only syntax parsing/compilation and `--help` were checked for this adapter. It was **not run against a host or used to claim another acceptance pass**. Original normal-host evidence and its source labels remain authoritative. `adapter-validation.json` records that boundary.

## Transfer and verification

From the original evidence root, the archive was created only with the explicit allowlist:

```sh
tar -czf /outside/path/round6-correctness-portable.tar.gz -T portable/allowlist.txt
```

The portable manifest includes every payload file except its own hash. The archive entry set must equal `allowlist.txt`; no `timer-host`, `identity.db`, `.db-wal`, `.db-shm`, private configuration or symlink can be included. Original archived stores are integrity-checked on temporary decompressed copies using immutable read-only SQLite. Original source stores and original script/report bytes remain unchanged.
