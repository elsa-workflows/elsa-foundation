# GitBook source setup and recovery

This runbook is for Elsa 4 documentation maintainers. It lives outside `website/docs/`, the published content root. Preserve the Elsa 3 site at [docs.elsaworkflows.io](https://docs.elsaworkflows.io/) and its Git Sync configuration.

## Current configuration

The owner published the site and enabled Git Sync on 11 October 2026, then configured [v4.elsaworkflows.io](https://v4.elsaworkflows.io/). These are observed settings, not instructions to reconnect the live site:

| Setting | Observed value |
| --- | --- |
| Organization | Elsa Workflows |
| Site / content space | `site_kpGrc` / `gq3cpwGkKOuvVeJzeNZN` |
| Source | `elsa-workflows/elsa-foundation`, branch `main` |
| Project directory | Repository root (empty field, equivalent to `./`) |
| Public address | `https://v4.elsaworkflows.io/` |
| Account signals | Site marked Sponsored; public footer identifies the free Community Plan |
| Fork previews | Disabled when inspected on 11 October 2026 |

The authenticated overview reported **Published / Public / Synced**. All seven pages, code blocks and the workflow diagram were visually checked on the GitBook domain. Separate direct HTTPS checks verified all seven pages, canonical URLs and image endpoints on the custom domain. Full plan entitlements, multi-space limits and external-contributor preview access remain unverified.

Evidence: [hosted checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2498#issuecomment-6113578105), [custom-domain checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2498#issuecomment-6113922514). Source preparation landed through #2641 and #2643; the latter merged as `d4d7b6bbcc814d3b8229c933931115ccefe116ec`. These observations do not prove every later revision has synced.

## Source mapping and navigation

Site Git Sync reads `gitbook-docs.yaml` at the repository root. It maps the default space to the repository root (`content.directory: ./`). The separate `.gitbook.yaml` directs that space to `website/docs/`; `structure.readme` and `structure.summary` identify the landing and navigation files relative to that content root. Keep both configurations. The site filename has a hyphen and no leading dot.

The logical mapping key `elsa-4-docs` is permanent after the first sync. Changing a live key can replace its space; it is not a rollback mechanism. Review any GitBook-generated configuration changes through the content review path.

Link to canonical source guides instead of copying their explanations. Current-development source links must describe the actual `main` tree. During Foundation/Studio consolidation, update them only after the new paths land and their targets are verified. Preserve immutable quickstart links and image digests: they document a specific historical pair and environment.

GitBook's [content configuration](https://gitbook.com/docs/docs-as-code/git-sync/content-configuration) explains YAML mapping and `SUMMARY.md` navigation.

## Contribution and publishing boundary

Contributors edit Foundation Markdown and submit a GitHub pull request. Each public page includes **Edit this page on GitHub**, pointing to its owning file on `main`. GitHub may require sign-in or a fork; editing a file is not permission to commit directly to the live branch. Opening a PR does not require a GitBook editor seat. Review claims, links, navigation and the exact preview revision before accepting a change.

GitBook documents [PR previews and publication after merge](https://gitbook.com/docs/getting-started/quickstart) and [two-way Git Sync](https://gitbook.com/docs/guides/editing-and-publishing-documentation/import-or-migrate-your-content-to-gitbook-with-git-sync). Treat a merge to the configured branch as publication-impacting. GitBook editor actions can also write back to GitHub; sync does not itself establish a required GitHub review.

Read-only GitHub inspection on 12 October 2026 found effective `main` rules requiring **Build & test** and **Generated maps fresh**, plus deletion and non-fast-forward restrictions. They did not require a PR or reviewer approval. A classic branch-protection 404 does not mean no rules exist: inspect rulesets too. The intended GitHub review path is therefore not proven to be enforced for all GitBook-originated writes. Do not claim it is protected, and avoid direct GitBook editor changes until the owner selects and verifies the write-back/review policy. This runbook does not authorize a settings change.

Check actual account controls before relying on them. GitBook's [member roles](https://gitbook.com/docs/collaborate/member-management/roles) describe editing permissions, but do not establish this account's seat entitlements or external-preview access. Do not invite members, purchase seats or enable fork previews merely to obtain a passing check.

## Safe preview update and revert check

Use an unmerged Foundation review branch; keep the live source branch and mapping unchanged. A retained local trial branch does not establish that its remote branch still exists or is the live source.

1. Record the live text, `main` SHA, site/space identifiers and mapping keys. Create a separate branch from that source checkpoint.
2. Make a harmless, identifiable text change in one page. Review it locally, commit it and open a draft Foundation PR. Do not merge it or reconnect the live site to it.
3. Inspect the exact-head GitBook check/status or PR comment for a preview URL. If none is produced, record that limitation. A local branch or HTTP 200 is not preview-sync proof.
4. Open the supplied preview and verify the marker, navigation, code and image asset. Record the commit SHA, preview URL, account/access context, date and screenshot.
5. Revert the marker commit with a normal follow-up commit. Verify the preview shows the original text at the revert SHA. Confirm the live page and source mapping stayed unchanged.
6. Retain both commits and observations. Close or retain the test PR according to its agreed review scope; do not delete shared branches without authority.

A preview update/revert qualifies the branch-preview path. It does not prove recovery of already-published content or GitBook-originated write-back. Keep those results distinct.

On 12 October 2026, draft PR #2645 exercised this path: marker commit `26823005fff6b11ec1586786dc188fca8a6bd972` appeared in its GitBook revision's main HTML; normal revert `713d3131b3c5262de97a4d56210765cdb803fa81` removed it. All seven owning edit links, five code blocks and the workflow asset were served from the revert preview. Live-page checks before and after the revert found no marker, and `main` remained unchanged at readback. This was HTTP access without cookies; preview browser navigation timed out, so fresh visual rendering and an outside-contributor identity were not verified. See the [exact revision and commit evidence](https://github.com/elsa-workflows/elsa-foundation/issues/2498#issuecomment-6114699971).

Fork previews are an observed access limit, not a successful test. With them disabled, use the GitHub diff as the external contributor's review surface. An actual external-fork preview needs a separately authorized test under the selected policy and an outside contributor or signed-out access check; an organization member testing their own fork is not equivalent evidence.

## Recovery and operational handover

Before accepting a publishing change, identify the maintenance owner, source branch and SHA, GitHub reviewer, publishing trigger, expected site/space and recovery operator. Confirm merge authority separately; green checks or enabled sync do not confer it.

For a bad accepted content change:

1. Identify the introducing commit and last known good published text. Preserve failure evidence.
2. Prepare a normal revert PR on the configured source branch and run affected documentation checks. Review configuration changes carefully; preserve the permanent mapping key.
3. Obtain the usual merge authority. After the revert lands, wait for sync and verify expected public content, navigation, code and assets. Record source and hosted evidence.
4. If sync fails, inspect GitBook status and logs before reconnecting. Initial import can overwrite content; never use a direction swap as improvised recovery.

GitBook also documents [change requests and restoring version history](https://gitbook.com/docs/guides/docs-best-practices/make-your-documentation-process-more-collaborative-with-change-requests). A GitBook history restore can interact with two-way sync; do not use it until its GitHub effect and review policy have been verified. Published content recovery and GitBook-originated write-back have not yet been exercised for this site.

The owner configured the custom domain. DNS recovery is separate: capture its current target and TTL with the authorized DNS operator before any future change, then prepare restoration and DNS/HTTPS checks. Do not infer a previous record or tested DNS rollback from a working domain. Preserve Elsa 3 addresses and sync throughout.

## Validation and outstanding evidence

Before proposing a change, validate `gitbook-docs.yaml` against its schema, verify both YAML directory mappings, confirm every `SUMMARY.md` page exists, and check relative links/assets and per-page GitHub edit targets. Review the synced exact revision when a preview is available.

| Check | Evidence state |
| --- | --- |
| Initial live sync, seven-page rendering and custom-domain HTTPS | Observed; checkpoint links above |
| Same-repository PR preview update/revert | Served-HTML update/revert observed in #2645; fresh visual check pending |
| External-fork preview and outside-contributor access | Unverified; fork previews observed disabled |
| Direct owning-repository edit links | Seven targets served in #2645 preview; live proof pending merge/sync |
| GitBook-originated write-back and enforced review | Unverified; effective `main` rules do not require PR approval |
| Published content revert / DNS recovery | Procedures prepared; not exercised |

Keep #2498 open while required proof is unresolved. Record restrictions and failures beside passes; do not relabel unavailable hosted checks as successful local validation.
