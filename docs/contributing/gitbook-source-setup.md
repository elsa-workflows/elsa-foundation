# GitBook source setup

This runbook is for the person configuring GitBook. It is intentionally outside `website/docs/`, which is the published content root. The trial is a separate Elsa 4 space; preserve the existing Elsa 3 space and its Git Sync configuration.

## Source and navigation

Site Git Sync reads `gitbook-docs.yaml` at the repository root. This maps one default Elsa 4 space to the repository root (`content.directory: ./`). The separate `.gitbook.yaml` tells that space to read `website/docs/`; `structure.readme` and `structure.summary` identify the landing page and navigation file inside that content root. Keep both files: renaming `.gitbook.yaml` does not create a site mapping. The site filename has a hyphen and no leading dot.

Keep the source tree in GitHub and review content changes through Foundation pull requests.

GitBook's [content configuration](https://gitbook.com/docs/docs-as-code/git-sync/content-configuration.md) documents these paths and the `SUMMARY.md` format. Paths under `structure` are relative to the configured root.

## Prepare a safe Elsa 4 trial

1. Create or select a dedicated, empty Elsa 4 trial site. Before initial import, verify it has no content to preserve. Do not select the Elsa 3 site or a site with existing content: initial sync selection can replace content. The committed site mapping creates the trial space during import.
2. Set the Git Sync **Project directory** to the Elsa Foundation repository root (`./`), not to `website/docs/`. The root `gitbook-docs.yaml` maps the default space to that same directory; `.gitbook.yaml` narrows its content to `website/docs/`.
3. Verify that the isolated trial branch `claude/2498-foundation-gitbook-source` exists on GitHub at the reviewed head before selecting it. PR #2641 merged the initial source; a merged PR does not prove its branch still exists. If the branch is absent, coordinate restoration from the retained local branch with the current Foundation checkpoint owner. Do not substitute `main`. Select the trial branch and the initial **GitHub to GitBook** direction. Do not choose **GitBook to GitHub**. The YAML directs GitBook to `website/docs/`.
4. Do not purchase or upgrade a plan, change DNS, or publish the trial site as part of this setup.

The initial space key is `elsa-4-docs`, a logical mapping key selected for this empty site, rather than a guessed GitBook site/space ID. Treat it as permanent after the first sync. If GitBook updates `gitbook-docs.yaml`, inspect and review the changes on the trial branch. Do not change a live mapping key: GitBook treats a changed key as a replacement space.

GitBook describes Git Sync as two-way; do not assume sync itself creates or requires a GitHub pull request. Keep GitHub pull requests as the accepted content review path. Avoid direct editor changes unless the write-back behavior and a protected branch or change-request path have been explicitly configured and verified.

Before connecting, record the actual organization, site/space identifiers, current plan, custom-domain and multi-space entitlements, editor-seat terms, and external-fork preview settings. Record observations from the account; public pricing alone does not establish its entitlements. Do not purchase a plan or expand access to complete the trial. Capture the existing Elsa 3 URL and sync target read-only for the preservation comparison.

## Hosted proof checklist

Record each result from the dedicated trial space; local checks do not substitute for hosted proof:

- Rendered navigation matches `website/docs/SUMMARY.md`, including Essentials, Extensions, Studio, and Contributing.
- Markdown headings, fenced code blocks, and any inline code render correctly.
- Relative page links and image assets resolve in the hosted pages; external source links open their intended destinations.
- A reviewed branch update appears in the trial space, and a follow-up revert removes that change.
- Any GitHub “edit source” links point to the intended Foundation repository, branch, and file path.
- Preview access is checked for a contributor outside the Elsa organization or a signed-out reader where possible. Record the GitBook account, membership, and fork restrictions that affect external contributors; do not promise preview availability.
- The existing Elsa 3 space and sync remain unchanged, and the trial has not written to `main`.

GitBook's [Git Sync import guide](https://gitbook.com/docs/guides/editing-and-publishing-documentation/import-or-migrate-your-content-to-gitbook-with-git-sync) describes repository connection and initial sync directions. Connecting GitBook and qualifying the trial require an operator with the necessary account access. The existing Elsa 3 space, plan, DNS, publishers, and `main` remain outside this setup.

## Record evidence and test rollback

For each hosted check, record the source SHA, synced branch, page or preview URL, observed result, date, and relevant screenshot. Preserve failures and access restrictions alongside passes. An HTTP success response or local audit does not establish that GitBook rendered the expected revision.

On the isolated trial branch, make one reviewed, harmless text update, observe it in the hosted space, then revert that commit through the same reviewed path. Verify the original text returns, navigation/assets still resolve, and the space identifier and mapping keys remain unchanged. Keep both commit identifiers and the hosted observations. Do not use a mapping-key change as a rollback mechanism.

## Prepare the publishing handover

Before requesting production authority, present the actual site and space, configured source branch, review path, publisher behavior, maintenance owner, and verified rollback results. Explain which GitHub merges or GitBook editor actions publish content and which observed controls prevent unreviewed writes.

The target public address is `v4.elsaworkflows.io`. Obtain the exact DNS target from GitBook's [custom-domain setup](https://gitbook.com/docs/publish/custom-domain.md); do not guess it. Before any separately authorized DNS change, capture the existing record and TTL, identify the rollback operator, and prepare restoration and DNS/HTTPS verification steps. Preserve all Elsa 3 URLs and Git Sync settings.

For a content rollback, revert the accepted content change on the configured source branch through its review path and verify the resulting hosted revision, links and assets. DNS rollback is a separate operation using the captured pre-change record. A successful trial content revert does not prove DNS recovery or authorize a main merge or public launch.

## Local review

Before proposing a content change, validate `gitbook-docs.yaml` against its declared schema, verify both YAML files' directory mapping, confirm every page in `SUMMARY.md` exists under `website/docs/`, and check relative Markdown links and image paths within that content root. Review the resulting GitBook preview after sync; local checks do not prove hosted rendering or publication.
