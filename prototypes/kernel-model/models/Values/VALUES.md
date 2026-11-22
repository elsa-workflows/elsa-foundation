# Values by reference experiment

The opt-in execution model for design decision 10 stores a length and SHA-256 digest in its checkpoint and Full journal. A put on an existing kernel job ticket creates the file in an experiment-owned directory. The model first learns the reference from the successful job result. A read on a later job verifies both digest and length before the model finishes. Separate worker, model and file-store instances perform recovery. The inline control stores the equivalent raw input as base64 in its checkpoint and journal. Both inputs use deterministic pseudorandom bytes. To keep the fault and size comparisons repeatable, the synthetic producer encodes the seed and length in the put ticket. Passing an existing large byte array in that ticket would add its own kernel traffic.

Run from the repository root with `dotnet run -c Release --project prototypes/kernel-model/models/Values.Run`. The host creates a temporary directory and databases. It removes that directory when the run ends.

| Raw value | Representation | Sum of UTF-8 state bytes | Sum of UTF-8 journal-entry bytes |
|---|---|---:|---:|
| 4 KiB | Inline | 59,351 | 58,930 |
| 4 KiB | Reference | 1,351 | 930 |
| 100 KiB | Inline | 1,469,721 | 1,469,300 |
| 100 KiB | Reference | 1,371 | 950 |

Each row sums ten actual SQLite-committed value-bearing revisions, checked against the expected value each time. The first stores the value. Nine additional updates retain it and explicitly journal the same value in the same Full-history shape. The sums count serialized UTF-8 fields, including base64 and JSON escaping. They exclude row metadata and the initial put-intent turn. The existing size benchmark covers inline state growth on four engines. The PostgreSQL WAL and TOAST measurements in `WRITES.md` concern earlier workloads. This run measures neither WAL, physical pages, TOAST, elapsed performance nor PostgreSQL network trips. [Inline benchmark #4360](https://github.com/elsa-workflows/elsa-foundation/pull/4360)

| Stage | Inline SQLite engine calls | Reference SQLite engine calls |
|---|---:|---:|
| Create through first value-bearing commit | 5 | 13 |
| Next nine updates, queue hot | 45 | 45 |
| Add read request from outside after handoff | 1 | 1 |
| Fresh owner processes read through finish | 12 | 20 |

The counter sums EF commands, transaction begins and ends inside worker passes, plus the explicit outside add. Setup, lease take/release, initial poll, independent checkpoint/journal verification queries and final independent byte-for-byte read are outside these numbers. Reference mode also performs one blob put, including one verification read, and one fresh-owner read. An uncontended new put writes one temporary file, flushes it and renames it. These are logical value-store counts and describe a different cost from database engine calls. The inline control performs no blob I/O.

The measurement and fault harnesses drive the workers and their continuations on one exclusive task scheduler, as the runtime host does. File jobs run on the default scheduler. The fault harness uses an in-memory kernel store with the same filesystem value store. It holds the first job's acknowledgement after the object exists, confirms that no reference is checkpointed yet, advances past lease expiry and executes the durable put ticket on a new owner. Both puts produce one immutable object. With its local clock held while store time advances, the former owner receives the late put result and runs its turn. The stale flush loses ownership and leaves the replacement’s checkpoint, journal, partition position and empty durable message set intact. Missing files, truncated content and equal-length corruption fault the read while preserving the previously committed reference. A separate injected storage refusal after a successful put leaves the object without a committed reference in checkpoint or journal.

The filesystem layout is a private synthetic namespace. Same-directory atomic rename, content verification and worker takeover are exercised. Filesystem crash durability, directory fsync, external-service availability, tenant scoping, encryption, cross-tenant deduplication, pruning, reference tracking and erasure are unqualified. An orphan and an object still reachable from historical journal rows have different retention needs. Their lifecycle is a design-proposal question. No default threshold, automatic offloading, segment layout or garbage collector is selected here.
