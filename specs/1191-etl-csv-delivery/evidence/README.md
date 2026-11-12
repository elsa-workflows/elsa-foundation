# Evidence: Finite CSV ETL delivery

Every receipt names the source it ran on. Limits are stated with each one.

## Actual Workbench journey (SC-006, US4)

The driver is [`csv_workbench_journey.py`](csv_workbench_journey.py), derived from spec 1190's driver, which is left unchanged as that spec's evidence. It ran at source `eb418a835` (clean tree), on a Debug Workbench built from that commit (.NET SDK 10.0.402), with Python 3.14. The owned host ran on 127.0.0.1:5393; the driver starts only that process and stops only that process.

It publishes two JSON Lines → CSV pipelines (both routes CSV, one record per batch, two partitions) and runs each from a native Flowchart. Python's `csv` module parses every published file. The derived summary is [`workbench-journey-summary.json`](workbench-journey-summary.json); the host URL and process id are removed from it.

| Case | Native outcome | Main CSV | Error CSV |
|---|---|---|---|
| clean: values with a comma and quotes, a leading space, and an embedded LF | `Completed` through `Done` | 4 files, each beginning with `id,name`. Rows read back exactly: `Ada, "the first"`, `Bram`, ` Cleo`, `Dee⏎Line`. No `.jsonl` files. | none |
| dirty: Ada first and Eve last, with one bad line of each kind between them | `Completed` through `BusinessError` | rows `1,Ada` and `7,Eve` | header `recordOrdinal,code,field`, with rows `2,etl.jsonl.record-invalid,`, `3,etl.jsonl.record-not-object,`, `4,etl.invalid-value,id`, `5,etl.required-value-missing,id` and `6,etl.jsonl.record-invalid,` |

Every file is BOM-free and ends in CRLF. Both runs leave zero outstanding records and bytes. Inspection reports the main and error kinds as `csv` and contains no host path.

**Limits:**
- The journey does not kill the host. Kill points and a conflicting file are proved in real worker processes, below.
- SQLite stores only.

## Real browser against the owned hosts (SC-005, US3)

An owned `Elsa.Studio.Web`, built from the same commit, ran on 127.0.0.1:5394 against the owned Workbench and was used through the built-in browser:

- [`studio/live-csv-routes.jpg`](studio/live-csv-routes.jpg): the saved draft shows both routes as **CSV file**. Their pickers read "Choose a host-approved accepted/rejected file output binding."
- [`studio/live-csv-run-inspection.jpg`](studio/live-csv-run-inspection.jpg) and [`studio/live-csv-run-inspection.txt`](studio/live-csv-run-inspection.txt) (captured page text): the dirty run shows **Source type JSON Lines**, **Main output type CSV file** and **Error output type CSV file**, 2 main and 5 error route bodies accepted, and both file ranges.

Both owned processes were stopped. The owner's Workbench on 5095 was not touched. The fixture-backed Playwright spec adds CSV route authoring at 320px and 1024px, with no axe violations: [320px](studio/1191-studio-csv-route-320.png), [1024px](studio/1191-studio-csv-route-1024.png).

## Worker processes (SC-003, US2)

`EtlRuntimeJourneyTests.Csv_worker_processes_replay_a_published_effect_or_refuse_a_conflicting_file` runs real child workers over one SQLite store:

- **Replay.** The predecessor is killed at `BeforeAcceptance`, after its CSV files are published. Each published file begins with its header. The replacement claims the same partition and batch under a higher fence. Every published file is byte-identical afterwards, and the run completes with 2 accepted, 1 rejected and 1 filtered, and nothing outstanding.
- **Conflict.** The predecessor is paused mid-way through writing its first CSV file to staging. A different file is placed at that effect's create-only key, and the predecessor is killed. The replacement's publication is refused with `etl.effect-conflict`, and the planted file is left byte-identical. The run settles through the shared diagnostic path, which classifies the conflict as a business error, as it already does for JSON Lines.

## Bite-proofs (SC-002)

[`bite-proofs.json`](bite-proofs.json) records each mutation at `eb418a835`, plus a round-1 and a round-3 mutation, each with its own source revision. For each one, the tests failed with the mutation in place and passed again after the file was restored from git.

| Mutation | Failing tests |
|---|---|
| CSV quoting disabled | 11/20 writer tests |
| Header not written | 1/1 |
| CRLF replaced by LF | 3/20 |
| Effect column storing the raw CSV kind (0, which breaks the constraint) | 1/1 |
| Acceptance computing a `.jsonl` key for a CSV route | 2/3 |
| Publication verified through the `.jsonl` store | 1/1 |
| HTTP array's closing bracket not reserved (bites only through the byte accounting; see the round-3 row) | 1/1 |
| CSV row bound loosened by one byte | 1/2 |
| HTTP array's first row bounded without its reserved bracket (round-1 fix) | 1/1 |
| HTTP array's closing bracket not reserved, with a file row that fills the limit exactly (round 3) | 2/2 |

An earlier quoting mutation only disabled the empty-string check, because of operator precedence. It was replaced, and the replacement is noted in the file.

## Artifact reviews

Independent reviews ran before implementation, and every finding was adopted.

- **Spec review.** It found one blocker: the transformer bounded each row's JSON object rather than its CSV row, and never counted the header. This is now FR-007 and research R2. Ten other findings were also fixed:
  - the effect-kind replay mapping;
  - the list of gates keyed on the JSON Lines kind, including the verification-store trap;
  - wording on value exactness;
  - quoting for error cells;
  - the single-column blank-line limitation;
  - the definition of whitespace;
  - the outcome of a conflicting file;
  - the Studio copy;
  - docs;
  - mixed versions.
- **Plan and tasks review.** It found eleven corrections:
  - output-processor tests at the right level;
  - the unreserved closing `]` for HTTP routes, an existing bug that is now fixed and tested;
  - the inspector tests rewritten against the mechanism constants;
  - missing scenario tests;
  - the inspection label;
  - doc comments;
  - the §E5 note;
  - the staging-gate use;
  - placement of the driver;
  - the task mapping;
  - a bite-proof for the verification store.

## Not covered here

- Configurable CSV dialects.
- Continuous runs, SQL and RabbitMQ.
- BPMN invocation of a CSV-delivering pipeline.
- Qualifying the ETL stores on databases other than SQLite.

These stay with #2908.
