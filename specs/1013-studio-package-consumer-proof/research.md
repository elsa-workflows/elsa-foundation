# Research: Package Consumer Controls

## Existing production proof is the base

Decision: extend `ApiPackageClosure.Probe`; reuse `HostingAssets.Probe`. Inspection confirms the latter already covers actual packages, isolated/locked consumers, browser/shared imports, resource/cache/range checks, customization and `--omit-published-vendor`. The #3362 audit passed its current-main production archives with140 standard/fallback resources and zero consumer frontend calls. A second host/probe framework would duplicate that behavior.

## Choose a dependency edge that cannot be masked

Decision: remove Api's Core dependency in a private copy. The existing Api-only consumer explicitly uses Core metadata and has no other Studio references. In contrast, a Hosting+Dashboard consumer can still get Api/Core through Dashboard, which makes removing one Hosting edge an invalid control. The historical #3367 failure already demonstrates the missing-Core compile diagnostic.

## Preserve inputs and distinguish failure causes

Decision: retain original Core/Api archives, mutate only Api nuspec dependency elements and record unchanged payload hashes. Refuse signed inputs instead of invalidating signature trust. Keep the normal failure exit/report, with a separate expected-failure observation bound to stage, graph and diagnostic. Do not treat arbitrary exit1 as a passing negative. Fresh directories and restored positives prevent cache/fixture effects from masquerading as causal evidence.

No unresolved research question or new library/technology selection remains. The [hosting contract](../../docs/reports/studio-hosting-contract.md), [spec1012](../1012-studio-browser-assets/plan.md) and current production tool source govern the boundary.
