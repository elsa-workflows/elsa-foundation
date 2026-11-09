# Rules foundation checkpoint

Reviewed implementation of tasks T004–T006 on `agent/codex/3181-durable-rules`, following planning commit `b006459df44404fe1b8b4e67bc3a92f920c5f9d9`. This is an internal implementation checkpoint, not a qualified durable model or PR review round.

## Implemented

- Core immutable finite-language, invocation, artifact, semantic-pin and result values with byte/collection snapshots.
- Persistence.Core command/query contracts, distinct semantic revisions and lease fences, immutable receipt and bounded reader values.
- Runtime canonical artifact/fact/receipt encoding, frozen-input evaluator, and complete receipt verification. Physical attempt metadata is excluded from the semantic receipt digest.
- Real projects and architecture assembly references; Runtime uses the conventional flat module path. No placeholder feature or runnable facade is advertised.

## Review and validation

A separate read-only review and root integration review found and repaired two semantic-input defects: artifact integrity must precede unsupported-version classification, and malformed JSON shapes must return Invalid instead of escaping the validator. Final recheck was clean, including the shared internal structural validation helper and project layout.

Commands executed against the restored checkpoint:

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests'
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
git diff --check
```

Results: 30 focused tests passed, zero failed/skipped; 85 architecture/API tests passed, zero failed/skipped; generated maps and solution-filter freshness passed; diff whitespace passed. Existing warnings in unrelated architecture-test files were retained. No solution-wide test result is claimed.

Removal control: temporarily replace the full receipt/hash comparison in `RuleRoundEvaluator.ValidateAsync` with an additions-only comparison. `Already_present_conclusions_still_require_their_firing_evidence` failed with expected Invalid / actual Equivalent. Restore the original source bytes and rerun the focused suite: all 30 passed. Restored evaluator SHA-256: `18b62754d15bc1fda1a61ad87d5579534068c34c424ae40325bd06ecccae9e1d`. This establishes only the complete-firing safeguard, not the other planned removal controls.

Raw local evidence: `/tmp/3181-round-removal-control.log`, `/tmp/3181-rules-restored.log`, `/tmp/3181-round-control-result.json`, `/tmp/3181-architecture-final.log`, `/tmp/3181-maps-final.log`, `/tmp/3181-foundation-gates.json`. These paths are local evidence locations, not durable public artifacts; the commands and outcomes above are the retained checkpoint record.

## Remaining

T007 boundary/budget proof and T008 actual service composition remain incomplete. Durable persistence, native continuation, actual package identity, process replacement, all four providers and additive upgrade/rollback have not yet been implemented or qualified. #3181 and the whole-leaf claim remain active; the broader program is open.
