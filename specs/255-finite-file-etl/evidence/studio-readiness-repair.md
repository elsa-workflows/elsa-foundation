# Supporting Studio CI readiness repair (#3081)

This is a supporting CI dependency of #3109 / draft PR #3111, not a new execution-model feature or program leaf.

## Reproduced cause

Studio run [39455236236](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39455236236), job118740253868, failed shuffled seed11 on the T022 head `ddb3f5a3652c7bc3c2dcc15d52a13a84f12d1fd2`. The custom-wait case in `workflowTestRunControls.test.tsx` passed a null query result into the native `HTMLInputElement` setter. TypeScript's non-null assertion supplied no runtime wait. This exact preexisting race was already recorded in [#3081](https://github.com/elsa-workflows/elsa-foundation/issues/3081).

The fixture now deliberately holds the waits request pending while the separately loaded runtime status visibly says `Waiting`. It asserts that no custom input exists at that point. Keeping the old interaction ordering reproduces the exact setter exception deterministically: **1 failed / 3 passed**. The ordinary pre-change focused run had passed; that passing run did not disprove the race.

The fix waits for the actual input/form and enabled submission, then for uncertainty plus retained attempt identity, restored input, and delivered feedback. The same payload and UUID assertions remain. The stop case awaits its actual view callback. No production code, arbitrary sleep, enlarged timeout, skipped assertion or workflow/protection setting changes.

## Integration and executed checks

Repair commit `427e709e88d2a50ea014493f9e48eba5cbc3a8a7` changes only the test file. Normal merge `265e1c7bdfb76cea60d79ed0943009235f35697c` integrates main `e656cc6042fd86d15d61c7278d846222054eb535` without conflict or history rewrite, preserving its two landed Studio changes. No ETL/backend source changes from T016 `1aacfc90977d8c579ff33f0ce8ef7406db13e24b`.

Before integration, all three shuffled seeds passed **163 files / 2,096 tests** each. CI's merge ref had four newer workflow-definition test files. After integrating them, **seeds1,11 and29 each passed 167 files / 2,124 tests**. The focused controls file passed4, targeted ESLint passed, Workflows typecheck passed both before and after integration, and frozen pnpm installation passed using Node22/pnpm11.9.0. The integrated dependency-map, solution-filter and diff checks passed. Root self-review completed three bounded passes, ending with no actionable finding in this repair. The pushed checkpoint receipt records the test source SHA256 and fresh exact-head CI separately; local passing checks do not imply CI completion.

```sh
pnpm install --frozen-lockfile
pnpm --filter @elsa-workflows/studio-workflows exec vitest run src/__tests__/workflowTestRunControls.test.tsx
pnpm --filter @elsa-workflows/studio-workflows exec vitest run --sequence.shuffle --sequence.seed=1
pnpm --filter @elsa-workflows/studio-workflows exec vitest run --sequence.shuffle --sequence.seed=11
pnpm --filter @elsa-workflows/studio-workflows exec vitest run --sequence.shuffle --sequence.seed=29
pnpm --filter @elsa-workflows/studio-workflows typecheck
```

Run these in `Studio/` with Node22. Targeted ESLint uses the repository's `Studio/eslint.config.js`. The deterministic delayed-waits failure and subsequent green file/suite results establish this readiness defect; they do not explain historical native receipt/HTTP timeouts or unrelated browser/main-red reports. Source and assertion review remains root-owned under standing self-review/normal self-merge authorization, without an independent-approval gate. Full ETL product acceptance remains incomplete; the PR stays draft.
