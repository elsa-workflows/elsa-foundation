# Qualification data model

No product entity or persistence schema changes.

- **Source identity**: repository, branch, exact commit/tree and built artifact hashes.
- **Journey receipt**: matrix row, selected source/build, observed identities/statuses, request classifications, screenshots and explicit limits.
- **Publication binding**: created draft, transient Test Run, saved/reviewed state/layout, promoted durable version and successful publication; opaque review token stays in memory.
- **Private lifecycle**: owned temporary root, disposable identities/configuration/fixtures, owned processes/contexts, cleanup and source immutability.
- **Review/delivery**: source round/head, findings/dispositions, required PR checks, separate approval/merge and resulting-main checks.

States stay separate: prepared → attempted → observed → root-reviewed → accepted. A failure, skip, unavailable state or unrun branch never advances itself to accepted.
