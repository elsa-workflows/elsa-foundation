# ETL fan-out fit spike

This deterministic in-memory experiment exercises the proposed execution-model seam. It is not production ETL, a storage-backed payload system, a global rate limiter, a default, or a new kernel guarantee. The range input stands for records addressed by a payload reference; each leaf contributes its one-based index to a count and sum. The tree aggregates child results. A coordinator persists its next cursor, child-id-to-range map, selected count and total, and its maximum outstanding children. It admits at most eight children at a time, using `Placement.Spread`; a direct baseline uses the same local credit with every leaf as a child of the root. Fanout 64 and credit 8 are experiment parameters, not kernel defaults.

Run from the repository root:

```sh
dotnet run --project prototypes/kernel-model/models/Etl.Run/Kernel.Etl.Run.csproj -- --size 4096
```

The runner uses real `Worker`, `WorkflowInstance` and `InMemoryStore` with four partitions. A wrapper lets a root result-selection commit land and then throws an unknown-outcome error. It expires the old leases without graceful release, takes all four partitions with new owners, and checks on the replacement's first root turn that the loaded cursor and pending map match the landed checkpoint. It then checks the final count and sum, every stored coordinator's local maximum credit, actual partition placement and payload references. `OnTurn` counts root turns and child-result messages; the instance count comes from the store. No global credit or durable external side-effect guarantee follows from this test.

At 4,096 leaves on this machine, the tree had 4,161 instances, 64 root result messages and 38 root turns; the direct baseline had 4,097 instances, 4,096 root result messages and 1,413 root turns. Both reached the expected count and sum and an observed local maximum of eight pending children. The tree numbers include the injected recovery, while the direct baseline has no fault; timings are not a budget. A 100,000-leaf run was started but stopped after four minutes under high shared-machine load, before it produced a result. These results do not prove a 100,000-leaf run, a real process death, a production provider, external data processing, or a performance budget.

Negative controls, each expected to exit nonzero:

```sh
dotnet run --no-build --project prototypes/kernel-model/models/Etl.Run/Kernel.Etl.Run.csproj -- --mutate-count
dotnet run --no-build --project prototypes/kernel-model/models/Etl.Run/Kernel.Etl.Run.csproj -- --mutate-credit
```

They respectively add each selected root result twice and admit one extra child. The run must catch the wrong total and exceeded local credit; the controls are local to this model.
