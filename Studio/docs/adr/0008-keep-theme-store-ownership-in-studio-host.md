# Keep Theme Store Ownership in Studio Host

Theme definitions, material texture assets, publication state, and default theme metadata belong to the Studio presentation experience, so the Theme Store is owned by `elsa-foundation-studio` and served by the Studio host. `elsa-foundation` remains out of scope for the first implementation because workflow/runtime domain state should not own Studio appearance unless a future product requirement introduces tenant-wide or backend-enforced theme policy.
