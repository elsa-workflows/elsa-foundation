# Validation guide

This is the planned proof guide. No HTTP feature or command result below is claimed executed yet.

1. Use the existing ETL shared fixture/WorkerHost with a real local controlled HTTP source and receiver, a fresh SQLite runtime database and isolated shared object root. Configure two finite shards, authenticated and anonymous aliases, and accepted/error destinations. Do not use production credentials or public endpoints.
2. Run focused codec/pagination/host-policy tests first, then HTTP store/admission/retry tests. Run the full `Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj` after integration. Serialize heavy tests and check machine load for timing-shaped failures.
3. Prove HTTP->HTTP, HTTP->JSONL and CSV->HTTP with typed transforms and optional lookup, including a fully filtered nonempty batch that advances without remote requests. Capture source/page/route identities and accepted counts using safe fixture observations, not secret-bearing logs.
4. Interrupt owned fixture processes after page publication, reservation, remote acceptance/lost response and before progress acceptance. Start a successor with the same database/object store and verify replay identities/no loss/no double accounting. Mutate each claimed fence/identity/backoff safeguard and confirm a targeted failure, then restore byte-exact source and rerun.
5. Build the Workbench and run ETL REST e2e with fresh databases; publish/select actual retained v1 and v2 pipelines inside Sequence and Flowchart. Separately migrate a pre-change runtime database with an owed/reserved file batch and resume under a successor, preserving identity and accounting. Run the ETL Studio authoring/inspection browser journey against that rebuilt host. Exercise business error, fault, cancellation and runtime-only execution with design storage unavailable.
6. Run affected full suites, architecture/core checks, generated-map freshness and solution filters, then inspect exact-head CI/Studio results and resulting-main runs. Scheduled-only skips remain skips. Finish the issue checklist and parent/project bookkeeping only on evidence.

Exact new test/runner paths are added as they are implemented; the existing spec255 command/evidence files remain the baseline. Do not mark the leaf complete using this guide or source inspection alone.

HTTP policy proof includes authenticated null-tenant refusal, anonymous endpoint policy, same-origin disallowed paths, and recording ETL logs/errors/spans separately from host-controlled platform HTTP telemetry.
