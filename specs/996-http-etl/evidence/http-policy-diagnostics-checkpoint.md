# HTTP policy, authentication and diagnostics checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), draft PR
[#3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156), task T017.
T017 is complete; **16/22 tasks** are complete. The HTTP leaf remains an incomplete draft.

## Actual retry authentication

`EtlRuntimeJourneyTests.HttpAuthentication.cs` drives the real connector execution, SQLite store and loopback
transport under an explicit tenant. The worker is stopped before admission so the fixture can advance a
controlled clock between durable attempts; it does not replace connector execution or HTTP transport.

- Source GET and destination POST first receive 503, then retry using newly resolved synthetic credentials.
  The second request carries the rotated token while the retained non-secret binding pin remains unchanged.
- Source capture retains the request hash and the successful attempt's capture provenance. Destination retry
  retains the same body and effect/idempotency key while the physical request attempt identity changes.
- Four cases revoke the connection or change non-secret settings before the retry, for source and destination.
  The next attempt refuses before sending, reports `etl.binding-unavailable`, retains its pin and previously
  owed fetch/output state, and cannot claim further work after terminal failure. It never falls back to anonymous.
- Resolver calls retain the exact tenant/name/type/owner, and every resolved connection is disposed.

These tests use synthetic in-memory connection providers behind the production interfaces. They qualify
point-of-use resolution and transport composition, not a cloud credential provider or a live identity service.
They do not make revocation atomic with a request already sent.

## Observed diagnostics

`EtlDiagnosticCapture` records actual logger messages, structured state, scopes and exceptions without masking
them in the fixture. It records platform activity tags, events, links and baggage under a test-specific trace.

The worker journey sends a query credential and an opaque continuation to a real receiver, processes a rejected
row, then receives a permanent HTTP rejection. It observes the safe error JSONL row and terminal diagnostic.
A result-delivery adapter throws an exception containing those synthetic values, its URI and an inner exception;
the actual worker warning must contain only the failure type. The fixture positively requires the warning,
rejected row, two wire requests and two platform request spans.

Transport cases send synthetic bearer/basic authorization and query credentials, verify the actual wire values,
and observe request spans and safe response metadata. Provider failures carry synthetic values but return only
the stable binding error without an inner exception or an extra wire request. The basic check includes both
clear username/password markers and the encoded credential.

ETL currently emits **no custom ETL spans**. Tests explicitly assert their absence, rather than claiming that
an empty span enumeration proves sanitization. Future ETL instrumentation must add positive assertions of the
same value-free contract. The plan and HTTP profile now state this distinction.

The .NET 10 platform request spans are positively observed with `url.full` containing origin/path plus `?*`.
Query values, authorization and payload markers are absent in the observed spans. Tests do not turn on redaction
or change global tracing configuration. The documented [.NET default](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities)
retains origin/path, so host exporters and additional instrumentation remain host responsibilities. These
fixtures do not claim protection against arbitrary host listeners or altered redaction configuration.

The Connections catalog now scopes `IRuntimeSecretMask` to the workflow adapter/activity fault boundary.
It does not require non-workflow ETL to import a workflow runtime mask or promise global logging protection.

## Existing policy acceptance retained

| Requirement | Executable coverage |
|---|---|
| Synchronous destination acknowledgement only | `EtlHttpDeliveryTransitionTests.Only_the_documented_success_statuses_acknowledge`, `Accepted_but_unacknowledged_and_permanent_statuses_fail`; actual `EtlConnectorOutputProcessorTests` 204/202 outcomes |
| Source status rules | `EtlHttpSourceFetchTransitionTests.Completion_applies_only_the_source_fetch_status_rules` |
| Exact origin/path, encoded separators and dot-segments | `HttpEndpointPolicyTests`; `EtlHttpRequestBuilderTests` next-link policy and relative canonical resolution |
| No redirects/cookie carryover | `EtlHttpTransportTests.Requests_do_not_follow_redirects_or_reuse_cookies_between_attempts` |
| Auth mutation/header collision refusal | `EtlHttpTransportTests` query/idempotency, authority/path and body mutation cases; `HostHttpBindingsTests` auth/transport header cases |
| Publication and scoped alias approval | `EtlDesignApiV2Tests` publication recheck/replay and scoped catalog cases; `EtlConnectorBindingPublicationTests` authenticated null-tenant refusal |

## Review and validation

Initial review found an empty custom-span assertion and a missing basic username marker; both were corrected.
The first integrated focused run caught a fixture assumption: a successful source capture uses `Capture`
provenance, not a generic status `Outcome`. The assertion was corrected against the actual store contract.
Root and independent review also required capturing all ETL log levels, rather than relying on the host's
Information threshold. The fixture now installs a provider-specific Trace threshold without changing the
host's other logger providers. The final nine affected tests were rebuilt and rerun after that refinement.
No production runtime change is proposed.

Two controls demonstrate that these are effective regression checks. Temporarily passing the raw exception
to the real worker logger makes the diagnostic test detect the synthetic query credential. Temporarily
removing retained-handle equality allows a second physical request after metadata drift; both source and
destination cases detect it. Both runtime files were restored byte-for-byte before final validation.

| Gate | Result |
|---|---|
| Full ETL suite | 965 passed, 0 failed, 0 skipped |
| Architecture suite | 584 passed, 0 failed, 0 skipped |
| Final affected authentication/diagnostic cases | 9 passed, 0 failed, 0 skipped |
| Logging and retained-pin regression controls | Expected failures: 1 / 2; production files restored exactly |
| Maps and solution filters | Passed; no generated changes |
| Relative links and diff | Passed |

Commands: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore`,
`dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore`,
`dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, and the same generator with
`solution-filters-check`. The affected test filter selects `Source_authentication_is_resolved_again`,
`Destination_authentication_is_resolved_again`, `Revocation_or_connection_settings_drift`,
`Http_diagnostics_`, and `Http_worker_diagnostics_`. The full suite preceded the final provider-threshold
refinement; the affected nine tests were rebuilt and rerun afterward. Bounded root/independent review is
clean. Exact-head CI and resulting-main verification remain final delivery gates.

T003 frozen predecessor execution, T018 operator inspection, T019 rebuilt Workbench/browser and T020–T022
final integration/delivery remain separate acceptance. Physical external effects remain at least once;
historical receipt/HTTP/SQLite-open timeout causes remain unproved.
