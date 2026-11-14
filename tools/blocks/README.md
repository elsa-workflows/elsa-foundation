# Native block prototype qualification

Supporting proof for [spec 1204](../../specs/1204-named-embedded-blocks/plan.md), S001. These tools use the actual normal Studio/Workbench composition, public authoring/publication/runtime APIs, fresh local stores and owned child processes. They do not implement the production extraction or promotion commands.

Build the actual products from a clean committed source, including the existing metadata observer:

```sh
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
(cd Studio && pnpm install --frozen-lockfile && pnpm build)
dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
dotnet build tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj
python3 tools/blocks/run_owned_prototype.py --output /tmp/elsa-blocks-owned-proof
```

Keep the controller running in its own terminal. It writes `ready.json` with the free loopback URLs, exact product versions/hashes and browser asset hashes. In another terminal:

```sh
python3 tools/blocks/qualify_native.py --control-dir /tmp/elsa-blocks-owned-proof
```

The normal development administrator is used by default; `ELSA_STUDIO_USER` and `ELSA_STUDIO_PASSWORD` can select an already configured test identity. Credentials and cookies are not written to receipts.

The native proof requires all 22 cases: 12 crossing-result comparisons over Sequence/linear Flowchart/BPMN, two loop comparisons with exact distinct values 1/2/3, four strict Balanced/Durable restart/resume comparisons, two cancellations with late delivery refused, and two supported native fault/reschedule comparisons. A result is `Passed` only after every assertion completes. An exception or `Running` receipt is incomplete. Existing evidence is never overwritten.

Cancellation requires the exact held activity occurrences to become Cancelled. Historical inspection bookmark counts may remain nonzero; they are not a live bookmark-index query. The proof requires zero late resumptions and no downstream execution, without claiming deletion of historical bookmark evidence.

The recovery fixture temporarily restricts the owned Workbench's dispatch depth to one. An exactly pinned nested dispatch faults during ordinary activity execution; the enclosing Sequence remains waiting under `ContinueWithIncidents`. The tool restores the original configuration, restarts only its owned backend and reschedules the exact faulted occurrence. It requires completion of the child, its Sequence ancestors and outer workflow, one downstream effect, a Superseded old occurrence and the retained original incident. It does not claim incident resolution. No CLR test activity or direct store mutation is used.

Use the emitted Studio URL for the separate browser authoring/history journey. Native runtime proof does not qualify extraction selection, BPMN interchange, read-only behavior or promotion. Those remain the acceptance rows of the production leaves.

Finish the controller through its command file:

```sh
python3 -c 'import json,pathlib; pathlib.Path("/tmp/elsa-blocks-owned-proof/command.json").write_text(json.dumps({"action":"finish"}))'
```

`closed.json` must confirm that every owned child stopped. Private stores/logs remain in that explicitly selected output directory for diagnosis. Never target another session's directory or stop processes by port/name.

## Production native planner qualification

`node tools/blocks/plan_native_cases.mjs INPUT.json OUTPUT.json` loads the production Studio planner through the installed Vite toolchain. Run it from a clean committed checkout with an input containing that `source` commit, the actual owned host `catalog`, and named `cases` containing a normalized `document`, optional `scope`, and extraction `request`. Use `operation: "create"` for empty creation or `refusal: "diagnostic-code"` for a negative case. Each accepted case includes the actual extraction and Ungroup documents for normal authored API publication/execution; the runner does not simulate runtime behavior. It refuses stale/dirty source and output overwrites and records input and planner hashes. Retain the input, output, source/product identities and paired normal API execution results with the task evidence.

`node tools/blocks/generate_result_transfer_fixtures.mjs --check` compares the current planner's bounded sibling-result documents with the committed golden fixtures. Omit `--check` to regenerate and review them. These are pure-planner documents with catalog-version placeholders, not runtime evidence. Workflows unit tests compare the exact documents; `NativeBlockResultTransferTests` resolves the actual owned host's catalog versions and exercises baseline/grouped/Ungroup publication, rejected naive scope crossings, outputs and cold restart. No package or result is published by the generator.
