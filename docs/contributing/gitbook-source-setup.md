# GitBook source setup

This runbook is for the person configuring GitBook. It is intentionally outside `website/docs/`, which is the published content root. The trial is a separate Elsa 4 space; preserve the existing Elsa 3 space and its Git Sync configuration.

## Source and navigation

The GitBook configuration is at the repository root in `.gitbook.yaml`. Its `root` points to `website/docs/`; `structure.readme` and `structure.summary` identify the landing page and navigation file inside that root. Keep the source tree in GitHub and review content changes through Foundation pull requests.

GitBook's [content configuration](https://gitbook.com/docs/docs-as-code/git-sync/content-configuration.md) documents these paths and the `SUMMARY.md` format. Paths under `structure` are relative to the configured root.

## Prepare a safe Elsa 4 trial

1. Create a dedicated, empty Elsa 4 trial space. Before connecting a repository, verify the selected space is empty and has no existing Git Sync. Do not select the Elsa 3 space or any space with existing content: initial sync selection can replace content.
2. Map the single trial space to the Elsa Foundation repository root, not to `website/docs/`. The root `.gitbook.yaml` selects `website/docs/` as this space's content directory.
3. Once `claude/2498-foundation-gitbook-source` is available on GitHub, select that branch and the initial **GitHub to GitBook** direction. Do not configure writes to `main` or choose **GitBook to GitHub**. The YAML directs GitBook to `website/docs/`.
4. Do not purchase or upgrade a plan, change DNS, or publish the trial site as part of this setup.

If GitBook creates or updates `gitbook-docs.yaml` for site-to-space mappings, inspect and review that file on the trial branch before accepting it. Do not add guessed site identifiers, and do not change stable mapping keys after the space is live.

GitBook describes Git Sync as two-way; do not assume sync itself creates or requires a GitHub pull request. Keep GitHub pull requests as the accepted content review path. Avoid direct editor changes unless the write-back behavior and a protected branch or change-request path have been explicitly configured and verified.

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

## Local review

Before proposing a content change, verify that `.gitbook.yaml` parses, every page in `SUMMARY.md` exists under `website/docs/`, and relative Markdown links and image paths resolve within that content root. Review the resulting GitBook preview after sync; local checks do not prove hosted rendering or publication.
