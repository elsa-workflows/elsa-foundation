# CI locked-restore repair

[CI run 39765000760](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39765000760), head `123ef7bcbef5ee9b34e50a1c2a35a7c872e80223`, failed before compilation with NU1004. The Modularity Tests and ProviderTests projects consume Workbench, but their committed lock files did not contain its new Rules project closure. Downstream architecture/core jobs were skipped, not passed.

Root reproduced the failure with a Release locked restore of `Elsa.Server.slnx` (exit 1), then ran an ordinary Release restore to regenerate the affected locks (exit 0). Exactly two lock files changed: each adds the six Rules project records and four Workbench project-reference edges. Existing external package versions and content hashes are unchanged. No source project, workflow, lock enforcement or dependency version was changed.

The actual CI restore script then passed with `--locked-mode -p:WarningsNotAsErrors=NU1603` (exit 0), covering Release and the isolated Debug graph. [Commands, durations and artifact hashes](ci-lock-repair.json) retain the failing and repaired evidence. This proves the restore repair; it does not represent compilation or hosted tests as passed. The next PR head still requires current hosted checks and review, followed by resulting-main verification after merge.
