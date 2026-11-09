# Rules bounded encoding checkpoint

Implementation follows foundation commit `1e1e44ba9ae47a93dc7c150a5816fca5a8a0c4f5`. This completes more of T007 and prepares T010/T012; it is not durable admission, a qualified installed package, or a PR review round.

## Source behavior

- Common invocation fingerprint includes exact scoped identity, opaque artifact bytes/hash/format, canonical initial facts, the frozen positive budget, full package/asset pin and every optional native route field. A separate versioned length-framed scoped identity preserves null, case, whitespace and distinct Unicode encodings.
- Common inspection verifies artifact byte integrity without interpreting the rule language. An unsupported language/pin can remain fingerprintable for cancellation/replay. Unknown common framing is still unsupported.
- Canonical encoding enforces actual byte caps, including digest fields in complete receipt/delivery records. The finite admission-capacity calculation counts the whole vocabulary and every firing, conservative encoded result identifiers/diagnostics, native routing, a separate delivery payload copy and the fixed-metadata reserve in the plan.
- Unsupported native result schemas remain common-inspectable but cannot pass the current new-admission capacity calculation. Installed-package availability and native destination proof are separate, still-unimplemented gates.
- Round decoding returns Invalid for oversized JSON integers and mismatched artifact metadata/body format rather than leaking a parsing exception or misclassifying corrupt data as an unsupported version.

## Review and executed checks

Separate read-only source review plus root review found and fixed unsupported native result-schema admission and possible duplicate terminal-payload undercounting. Recheck was clean within this scope. Tests also caught recursion in an initial MemoryStream span/array writer; the implementation now uses a bounded IBufferWriter. A test expectation was corrected to the default JSON encoder's `\u0022` quote escaping; production canonical encoding was preserved.

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests'
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```

Final results: 51 focused tests passed, zero failed/skipped; 85 architecture/API tests passed, zero failed/skipped; map and solution-filter freshness passed. Existing unrelated architecture-test warnings remain. The test fixture's package pins are explicitly synthetic encoding/semantics fixtures, not package qualification.

Boundary checks include exact and one-over 1 MiB canonical artifacts with JSON escaping, actual 4 MiB invocation encoding, vocabulary/rule/premise/UTF-8 identifier/round limits, bounded enumeration and exact/one-over writer limits. Shared finite-witness setup avoids repeated semantic fixtures.

Two actual source-removal controls were executed and restored:

1. Remove the bounded buffer's encoded-byte check. The actual invocation test fails because one-byte-over input no longer throws. Restored writer SHA-256: `03f1744f9ca98e86efac554fc31775a7b27b66e50e79e940a094585a4935d92e`.
2. Weaken receipt verification to additions-only comparison. The complete-firing test fails with expected Invalid / actual Equivalent. Restored evaluator SHA-256: `1b8fd0e7e37b4c854f77129da064b6c719ba398bd5e05e7f2e36552b538a4481`.

The restored suite passed 49 tests at the removal-control checkpoint; two final unsupported-result-schema cases bring the final passing count to 51. These controls do not establish fixed-point/budget durable settlement, cancellation, fencing, package identity or native acknowledgement.

Local raw logs: `/tmp/3181-admission-tests.log`, `/tmp/3181-encoding-removal-control.log`, `/tmp/3181-encoding-control-result.json`, `/tmp/3181-round-removal-control.log`, `/tmp/3181-round-control-result.json`, `/tmp/3181-admission-architecture.log`, `/tmp/3181-admission-maps.log`, `/tmp/3181-admission-final-gates.json`. Commands/outcomes above are the retained public record; those raw paths are local.

## Next persistence boundary

EF remains dependent on Rules Core/Persistence.Core, not Runtime implementation. The actual consumer will need a narrow semantic admission seam, analogous to IRuleRoundValidator, while common bounded identity handling stays available independently of interpreter support. Avoid exposing an already-validated flag. Existing identity/fingerprint matches must replay before semantic/pin checks. Recheck a concurrently admitted/cancelled key after validation failure and after losing the unique-key race.

The 64 KiB metadata bound and complete projection accounting still require actual provider enforcement and tests. Shared EF module registration/migration patterns in Secrets are useful for four contexts; ETL's initial SQLite-only registration and host TimeProvider are precedents, not proof of the planned four-provider or trusted-provider-time requirements. Do not copy its Design dependency into Rules persistence.

T007 remains open for fixed-point/budget settlement proof; T008 remains open for real service composition. All durable/provider/package/process/native/upgrade qualification remains pending. The sole whole-leaf claim remains active.
