# Scoped HTTP bindings and v2 authoring checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), single draft
[PR #3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156). This checkpoint completes T006 and
brings forward the API portion of T011. It does not enable HTTP worker execution or close the full leaf.

## Implemented boundary

- Added optional typed connector catalog/resolver ports without changing the file-only v1 catalog. Current
  alias/role collisions across file and HTTP kinds are invalid; typed resolution cannot approve the wrong kind.
- Host HTTP options snapshot scope, revision, endpoint policy and bounded shard seeds. Exact retained resolution
  never substitutes a newer current revision. The existing opaque handle commits endpoint policy, shard order,
  connection identity/type/scheme, all public settings and effective host policy; persisted pin DTOs are unchanged.
- Anonymous aliases work in exact null-tenant scope without Connections services. Authenticated aliases require
  an explicit retained tenant and matching ready connection, supported scheme and exactly one authenticator.
  No credentials are read while validating or pinning. Connections are short-lived, disposed on success/refusal,
  and provider failures become value-free diagnostics. Cancellation is honored before and after resolution.
- Endpoint policy uses exact canonical origin/path checks, strict explicit allowlists, seed-path defaults,
  HTTPS by default, controlled HTTP opt-in, and no user-info/fragments or ambiguous encoded separators.
  Reserved transport and authentication header conflicts are refused in host configuration/approval.
- Added `/etl/v2/...` definition/binding/validation/publication APIs with existing permissions and a separate
  capability declaration. V1 DTOs and catalog stay file-only. New publication rechecks approval; exact immutable
  replay works after a later draft edit or approval withdrawal without another binding resolution.
- Shared identity/history remains format-aware. Bounded signed cursors advance over raw rows even when a filtered
  page is empty, so old v1 publications remain readable after v2 upgrade. A v1-only provider still returns 404
  for missing history without requiring the optional v2 store. Common authoring helpers are shared.

## Review corrections

Root integration and bounded independent reviews corrected an overstrict literal-percent path check, unintended
widening of explicit path allowlists, omission of additional public settings from pins, unsafe provider cleanup
errors, a missing optional-store fallback, and incomplete reserved-header validation. Test execution also corrected
three fixture mistakes: a query-preserving URI expectation, a create request sent to an item URL, and an empty
foreign-scope catalog expectation despite that scope having its own seeded alias. No production behavior was
weakened to satisfy those expectations. An in-flight test edit initially produced a syntax error; the stable
candidate compiled before tests were evaluated.

## Validation

All commands ran from the same candidate worktree after restoring the three controlled source edits.

| Check | Result |
| --- | --- |
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore` | 862 passed, 0 failed, 0 skipped |
| `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter 'FullyQualifiedName~EtlDesignApiHttpTests'` | 18 passed, 0 failed, 0 skipped; rerun after adding explicit publish-permission and immutable replay-version assertions |
| `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore` | 584 passed, 0 failed, 0 skipped |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and `-- solution-filters` | Regenerated; only dependency-map changed, adding the public Connections Core dependency |
| Same generator with `-- check` and `-- solution-filters-check` | Passed |
| `dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore` | Passed, 0 errors; existing package/analyzer warnings remain |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1` | Passed against an owned rebuilt Workbench with fresh isolated storage; persisted Sequence and Flowchart both continued and produced expected JSONL |
| Relative evidence links and `git diff --check` | Passed before commit |

Each Workbench run retained two source partitions and reported two accepted, one rejected and one filtered record,
with two main chunks and one error chunk. The owned server shut down afterward. This proves existing file-ETL
compatibility through real HTTP, persistence and native runtime; it is not an HTTP connector execution proof.

Three focused controls removed retained-handle equality, exact-path authorization and new-publication approval,
one at a time. Each failed its corresponding regression; the handle control was repeated after deduplicating
cleanup helpers. SHA-256 comparisons verified byte-exact restoration before final validation. The retained-handle
control used `Exact_pin_rejects_drift_in_additional_public_connection_settings`; endpoint policy used
`Refuses_paths_and_origins_outside_exact_host_policy`; approval used
`Connector_publication_rechecks_approval_and_exact_replay_does_not_resolve_again`.

Root integration review and bounded independent contract/acceptance review found no remaining checkpoint blocker
after the corrections above. Exact-head CI is a separate pending gate after push; local success is not its receipt.

## Remaining acceptance

Actual HTTP GET/POST orchestration, request-local authentication and transport behavior, retry/capture/route
integration, CSV-to-HTTP and HTTP-to-file/HTTP execution, persisted native Sequence/Flowchart continuation,
Studio authoring/inspection, full two-process interruption and frozen old-worker resume remain owed. The existing
Workbench file journey is compatibility evidence only. Header/query collisions caused by authenticators and
observed transport telemetry remain T017; this checkpoint does not claim those execution checks.

Credential-source reference identity is not exposed by the Connections public port and remains part of permitted
rotation. A fresh configuration/revocation check cannot recall a request already sent. External effects remain
at least once. The historical receipt/HTTP timeout causes are not established by passing tests. The task runner
and workflow-scoped pause remain post-program follow-up.
