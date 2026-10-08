# Native GitBook sync trial protocol

This protocol is for the bounded native GitBook trial in [Foundation #2498](https://github.com/elsa-workflows/elsa-foundation/issues/2498), under [the Elsa 4 Website and Documentation program #2495](https://github.com/elsa-workflows/elsa-foundation/issues/2495). The small fixture is in [`content/`](content/).

## Boundary

The fixture is a local, non-production sample. It is not evidence that GitBook has synced, rendered, previewed, or published it. Native GitBook is the selected trial; this does not select the production generator or host. The completed local [Docusaurus spike #2496](https://github.com/elsa-workflows/elsa-foundation/issues/2496) remains available as the fallback for comparison.

Keep Elsa 3's existing GitBook space and repository untouched. Do not connect, publish, change DNS, buy or upgrade a plan, or edit any existing space as part of local fixture preparation. Trial outcomes and exact revisions belong on #2498. Do not put credentials, account identifiers, or private account screenshots in this repository.

## Official GitBook layout used by the fixture

GitBook documents `.gitbook.yaml` and `SUMMARY.md` as per-space files. For this trial, select the Foundation repository and branch, set the Git Sync **Project directory** to `tools/spikes/gitbook/content`, and connect one dedicated trial space using single-space Git Sync. The space's `.gitbook.yaml` has `root: ./`, so its README, summary, pages, and local assets all resolve inside `tools/spikes/gitbook/content/`. No repository-root GitBook site configuration is part of this single-space trial. Confirm the actual Project directory and content mapping in the GitBook UI and record the observed settings; the local fixture does not prove them.

References: [content configuration](https://gitbook.com/docs/docs-as-code/git-sync/content-configuration) and [monorepos](https://gitbook.com/docs/docs-as-code/git-sync/monorepos). GitBook says a mapped space syncs only its assigned directory and does not automatically share sibling content or assets; keep all fixture links and images local to `content/`.

## Operator sequence

Before connecting anything, confirm that the trial branch is available on GitHub, the sign-in/account access required by #2498 is available, and the target is a newly created isolated space. Record the account's actual plan and any feature gates in the ledger below. If GitBook requires a purchase, plan change, new account, or permission change, stop and record the exact requirement on #2498 for an owner decision.

1. In GitBook, connect only the dedicated trial space to `elsa-workflows/elsa-foundation`. Choose the trial branch and **Project directory `tools/spikes/gitbook/content`**. Use one-space Git Sync; leave the existing Elsa 3 GitBook site and all repository-root files out of scope. Keep the generated space key stable after creation.
2. Run the initial GitHub-to-GitBook sync. Confirm the three Markdown pages appear in `SUMMARY.md` order, both fenced examples render, the image loads, the same-space relative page and heading links resolve, and the source/edit links identify the intended owning Foundation files. Record the GitBook space URL, branch, commit SHA, page count, and any differences on #2498.
3. On the trial branch, make one small, clearly marked Markdown edit and sync again. Verify the rendered change and source revision. Revert that exact commit on the branch, sync once more, and verify the rendered content returns to the initial state. Do not amend unrelated content.
4. From an external GitHub fork, open a disposable PR that changes only a trial page. Check whether GitBook creates a preview, what account/seat is required, and whether the preview remains private to permitted reviewers. Do not merge the PR. Record unavailable preview behavior as a result, not as a passed check.
5. Make one harmless editor-originated change in the trial space, connected only to the isolated trial branch. Observe and record the exact GitHub branch/commit or PR behavior. Do not connect the editor to Foundation's default branch or assume GitBook's write behavior enforces GitHub review. Open a normal PR for the editor-originated change and verify that the diff can be reviewed under Foundation's usual gates; do not merge it. If GitBook provides a PR flow, record its behavior; if it commits directly to the trial branch, the separately reviewed PR is the evidence and the direct commit is not.
6. Inspect the actual account/site entitlements for the `v4.elsaworkflows.io` custom domain, site sections or multiple spaces, and external-fork PR previews. Record plan names and which features are available, gated, or unknown. Do not add the custom domain, change DNS, alter Elsa 3, or purchase anything.
7. Post the results, exact commit/PR links, limitations, and an evidence-based recommendation on #2498. Distinguish the local fixture from hosted sync proof and state whether Docusaurus remains the safer fallback.

## Trial ledger

Fill this ledger with observed values during the hosted trial and report them on #2498. “Pending” is not a pass.

| Check | Current evidence / result |
| --- | --- |
| GitBook sign-in and permitted account access | Pending. #2498 reports a sign-in screen and one request pending. |
| Actual account/site plan and editor-seat requirements | Pending; no plan or seat entitlement has been verified. |
| Custom domain `v4.elsaworkflows.io` entitlement | Pending; no custom-domain entitlement or DNS action is verified. |
| Site sections / multiple-space entitlement | Pending; no entitlement is verified. |
| Initial GitHub-to-GitBook sync from `tools/spikes/gitbook/content/` | Pending; local fixture only. |
| Markdown, code fences, local image, relative page and heading links | Pending hosted render; local paths are present in the fixture. |
| Source links and GitHub edit links | Pending hosted render and target resolution. |
| Update, then revert, sync round trip | Pending. |
| External-fork PR preview and account/seat requirements | Pending; no external fork or preview has been tested. |
| GitBook editor change returned through a reviewed GitHub PR | Pending; branch/commit and review behavior are unknown. |
| Final recommendation and Docusaurus comparison | Pending until the hosted evidence is recorded. |
