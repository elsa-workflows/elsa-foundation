# Effective persistence preview implementation evidence

Issue [#2177](https://github.com/elsa-workflows/elsa-foundation/issues/2177), implementation branch `codex/2177-effective-persistence-preview`, foundation base `d87a5bcb1291b76348ae48aa14740a73e9eb46b5`.

## Foundations: T001–T004

Executed on 2026-09-30 in the existing CLI test project. No new test project, provider container matrix or workflow change.

- Initial protocol/source baseline: 59 executed, 40 new cases red on missing candidate APIs, 18 pre-existing cases and one new legacy-reader control green.
- Host-response framing baseline: 27 executed, all red on the missing response reader after repairing the test harness compilation error.
- Capture owner baseline: seven executed, all red on missing ownership API.
- Independent capture review exposed opener/null-path sanitization: six new cases executed red, then fixed.
- Count admission before Build: one of two cases red on wrong refusal precedence. Identity admission: four of six cases red (129-character IDs, case collisions and cross-set overlap), while exact128 selected/removed controls passed. Fixed through shared finite selection admission before Build; worker retains independent admission and separate sorted-wire checks.
- Final focused foundation run: **146 executed, 146 passed, zero skipped**, with temporary reflection removed from the new response tests. Existing legacy request/source behavior controls remain green. Real supported-name unused Unix FIFOs refuse before content reads on macOS.

Reproduce the focused run:

```sh
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~WorkerProtocolTests|FullyQualifiedName~CompositionFileSourceTests|FullyQualifiedName~CompositionInspectionCaptureTests'
```

Root reviewed the complete foundation delta. Independent reviewers audited response framing and capture ownership/admission; reproduced findings were corrected. The frontend and worker are distinct assemblies, so their shared private-transport admission predicate requires public accessibility without adding friend assemblies or duplicating the protocol grammar.

## Meaningful foundation mutations

Each source mutation was restored in `finally`; after restoration, the 140-case run passed. Six additional null/excessive-selection admission cases then brought the final focused run to146 passing cases.

| Temporarily broken behavior | Executed | Failed as expected | Control passes |
|---|---:|---:|---:|
| Enforcing actual file-byte limit | 1 | 1 | 0 |
| Rechecking owned supplied intent before result | 2 | 1 | 1 |
| Correlating host invocation token | 16 | 1 | 15 |

These failures executed assertions against deliberately broken production behavior; they were not compilation failures.

## Still required

No acceptance matrix row is closed by these foundations. Host capability/operation, decoded file admission, shared builder removal materialization, installed-closure worker dispatch, full response contents, CLI command/formatting, real actor/runtime parity and child lifecycle/adverse proofs remain incomplete. T005–T020 and the full affected suites, architecture/maps, exact-head review/CI and exact-main delivery gates remain open. The specification stays **In progress**.
