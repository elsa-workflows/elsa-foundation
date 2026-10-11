# GitBook source setup

This runbook is for the person configuring GitBook. It is intentionally outside `website/docs/`, which is the published content root.

## Source and navigation

The GitBook configuration is at the repository root in `.gitbook.yaml`. Its `root` points to `website/docs/`; `structure.readme` and `structure.summary` identify the landing page and navigation file inside that root. Keep the source tree in GitHub and review content changes through Foundation pull requests.

GitBook's [content configuration](https://gitbook.com/docs/getting-started/git-sync/content-configuration) documents these paths and the `SUMMARY.md` format. Paths under `structure` are relative to the configured root.

## Connect a GitBook space

1. In GitBook, start Git Sync for the Elsa Foundation repository and select its repository root so GitBook can read the root `.gitbook.yaml`.
2. Select the intended branch and use the initial **GitHub to GitBook** direction to import the reviewed source. The YAML then directs GitBook to `website/docs/`.
3. Review the imported pages and navigation in GitBook preview. Confirm that the page tree matches `website/docs/SUMMARY.md` before an owner publishes or changes the live site.
4. Keep GitHub pull requests as the accepted content review path. GitBook describes Git Sync as two-way; do not assume sync itself creates or requires a GitHub pull request. Avoid direct editor changes unless the write-back behavior and branch protection or change-request path have been explicitly configured and verified.

GitBook's [Git Sync import guide](https://gitbook.com/docs/guides/editing-and-publishing-documentation/import-or-migrate-your-content-to-gitbook-with-git-sync) describes repository connection and initial sync directions. Connecting GitBook, selecting a live destination, publishing, and configuring domains are operator actions outside this source change.

## Local review

Before proposing a content change, verify that `.gitbook.yaml` parses, every page in `SUMMARY.md` exists under `website/docs/`, and relative Markdown links and image paths resolve within that content root. Review the resulting GitBook preview after sync; local checks do not prove hosted rendering or publication.
