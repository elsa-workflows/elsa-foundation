# Portable-input implementation evidence

Status: In progress under [#2461](https://github.com/elsa-workflows/elsa-foundation/issues/2461). This ledger records executed checks and their limits; the [task list](tasks.md) and [approved contract](contracts/portable-input-v1.md) retain the remaining obligations.

## Integrated foundation checkpoint

Production-source revision: `eee2e756a4fc162a80bdbacdb43a55bff5701b38`, pushed on `codex/2461-portable-private-inputs`. Root reviewed the two bounded worker commits, integrated them, extracted shared acceptance rules, and corrected review findings. The unchanged legacy acceptance tests remain the control for the extraction.

The implementation branch normally merged main `bfdde6ccdc4666479146177c1c845b7647111739`. Root reviewed the intervening four-file contributor-documentation delta and independently confirmed successful CI, Maps, Solution filters, Packages, Docker Images and Code Quality workflow statuses at that exact main revision. This is baseline qualification, not portability proof or a new package/artifact-content audit.

Independent cross-review and root review corrected two gaps:

- Complete receipt inventories now share the existing source basename grammar. The selected context retains its existing bounds; supported unselected siblings do not acquire a new 128-character limit. Regression controls cover a 129-character sibling through source capture and receipt creation/parsing/validation.
- Source recheck validates the bundle directory before and after enumeration. A byte-identical bundle exposed through a substituted directory link refuses. A separate write-path failure control stages one supported candidate file and then forces file creation to fail, requiring removal of unpublished staging and preservation of the source.

Independent review of the corrections found no further actionable finding. Workers used `gpt-6-luna` with `xhigh`; root reviewed integration and retains QA ownership. Runtime root model selection was unavailable; no orchestrator model switch is claimed.

## Executed checks

On 7 October 2026, root ran:

```text
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj --filter FullyQualifiedName~PortableCompositionTests -p:RestoreLockedMode=true --logger "trx;LogFileName=2461-portable-foundations.trx"
```

Locked restore succeeded. Result: **36 passed, 0 failed, 0 skipped**. This covers the integrated pure portable wire/correlation/public-content rules and the long-sibling regression. It does not exercise the produced portable commands or Workbench.

Root CLI verification completed against the same production-source revision:

```text
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj --no-restore --filter "FullyQualifiedName~CompositionFilePublisherTests|FullyQualifiedName~PortableCompositionCaptureTests|FullyQualifiedName~CompositionFileSourceTests|FullyQualifiedName~CompositionAcceptCliTests" --logger "trx;LogFileName=2461-cli-foundations.trx"
```

Result: **102 passed, 3 failed, 0 skipped, 105 total**. Duration reported by the test runner: 6 minutes 47 seconds, excluding the fixture build. This is a failed check, not a full CLI pass. The three unchanged controls failed at their bounded process/deadline assertions:

- `CompositionAcceptCliTests.Regular_file_check_refuses_directory_symlink_device_and_fifo_without_hanging`: the produced CLI exceeded the fixture's 5-second deadline.
- `CompositionFileSourceTests.Candidate_reader_rejects_a_fifo_replacing_a_regular_file_after_preflight_without_blocking`: the owned child exceeded its 30-second deadline.
- `CompositionAcceptCliTests.Workspace_profile_drives_plan_accept_and_generation_with_exact_readback`: the subsequent produced planning command returned timeout exit 124 after acceptance had succeeded.

The load averages observed immediately after the run were 768.41 / 743.36 / 719.96 on the shared 8-core machine. The failure shapes suggest contention; that does not prove the product correct or permit counting the failed controls as passed. Reconcile the failures with a quieter rerun before closing the corresponding foundation tasks or advancing a delivery gate. No deadlines were enlarged and no tests were removed.

The clean completed utility checkout was moved to the exact integrated revision, preserving its original worker branch and reusing its restored/build outputs. The shared build-slot wrapper remained in use. New long-sibling, root-link-swap and partial-write regressions were among the 102 passes; the source/publication/acceptance check as a whole remains red.

Earlier worker results apply only to their narrower source revisions: Planning final filter 35 passed/0 failed/0 skipped; utility/source filter 85 passed/0 failed/0 skipped before final worker hardening, followed by final publisher/capture filter 33 passed/0 failed/0 skipped. They do not substitute for root integrated verification.

## Unproven outcomes and next gate

Portable import, accept, rebind, generate and candidate/intended inspection command journeys are not delivered by this foundation checkpoint. No actual origin-unavailable transfer, replacement lifecycle, portable Workbench actor, privacy-bypass bite-proof, complete affected-suite pass, architecture/maps gate, PR review/CI or resulting-main portability qualification is claimed. Task completion must follow the corresponding root evidence.

Next: complete root CLI controls, close foundation findings and update task state, then implement safe portable output and the first actual import/accept/generate journey. Keep replacement and actual-byte intended inspection in the same owning issue before delivery.

The program also retains undelivered Authoring backend/client/profile work, production runtime builder, six-human usability study and trusted activation/recovery outcomes. This ledger establishes none of those outcomes.
