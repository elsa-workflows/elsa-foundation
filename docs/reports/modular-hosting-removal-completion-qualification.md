# Nuplane Removal-Completion Qualification

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md). Owning task: [Nuplane #109](https://github.com/valence-works/nuplane/issues/109), prerequisite of optional integration [CShells #142](https://github.com/valence-works/cshells/issues/142).

Status: locally implemented, root-reviewed and qualified on macOS; upstream review, CI, publication and adoption remain pending. No milestone is complete.

## Candidate and source review

The runtime fix and Feature 029 specification/plan/tasks are committed at `cbfa6eddd5f080a9d73d22554ac1ae39936c7dae` on `029-removal-completion`, based on Nuplane main `21e2c24fe8070a92ded13f7cc391353f7c6df856`. Final candidate `d6eac6e3faacb72b5acfb93c59d2862c340ca3a7` adds only a sample log-label correction and its specification evidence. Worktree `/tmp/hosting-nuplane-removal-completion` is clean.

`HealthAndMetricsMiddleware` now publishes the existing completion notification when successful applied packages exist **or committed removals exist**. The normal pipeline persists the next active state before Changed and Reconciled dispatch. The actual applied payload can be empty. Empty unchanged and failed-only cycles without removals stay quiet; failure/degraded accounting and observer cancellation/isolation remain unchanged. The public observer XML documents both empty-applied removals and existing successful-applied unchanged cycles.

Root and independent source review found no dispatch blocker. Review also corrected the sample's `ActivePackageCount` label to `AppliedPackageCount`; its authoritative discovery query is unchanged. This work adds no unloading/collection policy, package-use lease, pruning or Elsa dependency. Foundation #2362 retains actual collection/retire ownership. Delivery is not durable exactly-once across cancellation or process failure after persistence.

## Local verification

Worker focused tests passed: **10 middleware, 17 loading observer and 1 real file-backed removal integration test**. Six Nuplane/Abstractions net8/net9/net10 builds passed with zero warnings/errors. Worker observed the causal mutation: reverting only the removal condition failed the real integration regression on the missing completion callback; restoration passed. Worker stdout remains in its task transcript, not retained standalone logs.

Root ran the complete affected projects against the unchanged runtime/test source: **799 Runtime + 251 Loading + 156 Integration tests**, zero failed/skipped, Debug/net10. TRX and parsed counters are retained. The final sample-label-only commit does not change these tested runtime/test files; no sample build is claimed.

Root then built and packed Nuplane and Nuplane.Abstractions in Release for all three frameworks at private local version `0.0.11-preview.local.2500.removal`. Both nuspecs identify final candidate `d6eac6e`; Nuplane's internal dependency matches that version. Archives contain all three assemblies/XML docs and package assets. These are two local package candidates, not the owner's complete published package family or a remote release.

## External package consumer

An outside-checkout console consumer references only the packed Nuplane package, with Nuplane.Abstractions resolved transitively. Explicit source mapping permits `Nuplane*` only from the local feed; an isolated retained cache and assets graph match the actual archive SHA-512 values and byte content. There are no project references or source/assembly substitutions.

The real public `AddNuplane` composition seeds one logical active version in an owned file-backed store, then runs two real manual reconciliations. It verifies removal-to-empty, on-disk empty active state during the registered completion callback, the actual empty applied payload, exact Changed-before-Reconciled registration order, observer exception isolation and trailing delivery. The second empty unchanged cycle emits neither phase. Resolved state/install/package-lock paths, enabled store lock and disabled automatic reconciliation are asserted. No feeds are configured; offline mode is enabled; the unique package-install root remains empty.

The corrected fixture passed on actual **.NET 8.0.10, 9.0.9 and 10.0.8**. Its first successful run exposed an empty default package-lock file written into the working directory. Root preserved/moved that task-created file to the artifacts, configured an explicit temporary lock-file path, then reran all three targets. Corrected temporary roots were removed and the workspace stayed clean. Independent read-only package-proof QA found no concrete false positive or blocker.

This proves public package/DI notification and logical-state persistence. It does not prove physical package installation, collectible context death, pruning, a package-to-CShells serving-generation journey, or Foundation adoption. The optional adapter still needs a released Nuplane preview containing this fix and the published CShells primitives.

## Retained artifacts and remaining gates

Artifacts: `/Users/sipke/.codex-workspaces/artifacts/modular-hosting-2500/nuplane-removal-package-qualification/`. Retained files include the consumer/source-mapped configuration/script, corrected and initial run logs, package archives and SHA-256 manifest, package inspection/provenance, root TRX/counters, and source backup metadata. The repository is shallow: the tracked-source archive and two-commit format-patch preserve this candidate, but are **not** a complete-history Git bundle. Keep the checkout and refs.

Push/PR awaits the existing repository-required Git/session preference answer. Exact-head upstream CI/external review, merge/main publish pipeline, preview-feed/source identity and downstream integration/adoption remain outstanding. M3 shared-root admission and deletion-safety requirements remain separate. For optional integration recovery, use [D14](../plans/modular-hosting-upstream/decisions.md): retries require a later delivered eligible completion or an explicit requested build; quiet empty cycles do not provide automatic replay.
