# Cumulative review R2 checkpoint

**Source:** frozen `23058692a7366bbe805c745b888d5f0d0e33f4b1` against `origin/main` `fd0fdb21eacabe690183ff761432746e9c7e680c`. Five R2 findings have local corrections in `/tmp/elsa-3604-r2-fixes`; no final correction commit or independent approval is claimed.

| R2 finding | Local correction / supporting check |
| --- | --- |
| Same-revision resource responses could leave an obsolete disclosure projection in the editor | Reconcile current disclosure even when the saved revision is unchanged. |
| A question newly reused after opening the editor could be saved without reviewing current uses | Read current uses before Save and handle lost resource access. |
| Dashboard sharing/copying lacked identified question-dependency review and independent private question copy | Add the dependency review and explicit private-copy path; four dependency-review tests pass. |
| Container-free Dashboard ResourceTests was assigned to integration tier | Move only that project to PR shard `pr-3`; preserve ProviderTests/container and all other routing. Python selector/self-tests and manifest invariants pass. |
| Identity's canonical permission-contributor catalog omitted both new cross-domain implementations | List `DashboardResourcePermissionContributor` and `ProcessingPermissionExtension`, and document the Dashboard API contribution. Relative links, names and the contract anchor were checked. |

The editor regression run first failed five new cases while 17 existing cases passed (`/tmp/elsa-3604-r2-editor-red.log`). With local frontend corrections, the whole Dashboard suite passed 177/177 across 13 files (`/tmp/elsa-3604-r2-dashboard-tests.log`); TypeScript and targeted editor ESLint also passed (`/tmp/elsa-3604-r2-dashboard-typecheck.log`, `/tmp/elsa-3604-r2-editor-eslint.log`). These local results precede final qualification of the additional checkbox fix. The real maps/tier generator has not been rerun for the local tier correction.

Frozen 23058692a independently passed Architecture 618/618 without skips (`/tmp/elsa-3604-r1-architecture.log`, `/tmp/elsa-3604-r1-architecture/architecture.trx`). Package 005 passed 20 real archives and 19/19 native cases on that exact source, with `exactCommitQualified: true`, `independentPackageWitness: true`, `providerConfigured: false` and `fullFeatureQualified: false`. Its report contains private fixture credentials; publish only reviewed allowlisted evidence. These passes do not cover the later uncommitted corrections.

## Additional native 004 checkbox failure

Native 004 remains failed: `locator.check` reported that clicking the field checkbox did not change its state. The prebuilt frozen 23058692a host reproduced it. In the causal trace, focusing **Run kind** moved its checkbox top from 408.94 to 426.94 between pointer-down and pointer-up; the release/click landed on the surrounding `named-checks` container. The click missed the shifted input.

Injecting the candidate native CSS into the same prebuilt host made the targeted geometry checks pass at widths 1280, 360 and 320 (`/tmp/elsa-3604-checkbox-host-04.log`). The source fix is underway. This proves the targeted cause/candidate remedy only; a rebuilt final-source journey and inspected visual/keyboard states remain required.

Earlier native 001–003 and Architecture failures remain in the [evidence index](README.md) and linked stopped checkpoints. No full native journey, live-provider/independent UX acceptance, current CI pass, implementation merge or resulting-main qualification is claimed. Copilot model discovery remains 403/unavailable; no model prompt was sent.
