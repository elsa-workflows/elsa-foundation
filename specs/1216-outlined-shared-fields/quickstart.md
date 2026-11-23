# Validation guide

Install locked Studio dependencies with pnpm. Run the shared UI/host and affected optional-module Vitest suites, full frontend typecheck/lint and module builds. Run the shared Outlined-field browser gallery and affected existing browser suites with retry0. Inspect every declared theme/state capture, including transparent/custom surfaces, narrow/enlarged/RTL and accessibility modes.

For actual product acceptance run `python3 tools/ux-acceptance/run.py --output /tmp/outlined-4219-unique` on a clean committed checkout with owned disposable hosts, and retain the complete report/images/ARIA/traces. Use existing CI artifact uploads for downloadable evidence. Independent acceptance must open current captures. Required exact-head CI and containing-main proof complete delivery; a local fixture or source census does not.
