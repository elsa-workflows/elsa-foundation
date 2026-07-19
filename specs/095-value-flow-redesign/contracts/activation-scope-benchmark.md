# Activity Activation Scope Decision Contract

This contract defines the evidence required before Elsa selects the DI lifetime for transient CLR
activity activation. It does not select a strategy.

## Candidate strategies

1. One ambient service scope for the execution burst.
2. One ambient burst scope plus one child scope per CLR activity attempt.
3. A conditional fast path that omits a child scope only when it can prove the selected observable
   lifetime semantics remain unchanged.

## Required workloads

Each strategy is measured against the same executable workflow fixtures:

- a constructor-free no-op activity;
- an activity with transient constructor dependencies;
- an activity with a scoped disposable dependency whose transitive graph includes another scoped
  service;
- a long sequence dominated by engine-intrinsic control and value operations;
- a mixed sequence of intrinsic operations and micro-activities;
- an I/O-bound activity sequence;
- retry of one logical invocation;
- suspension followed by resumption in a fresh execution burst; and
- at least two workflow executions draining concurrently to detect scope contamination.

## Measurements

For each workload and strategy, the report records:

- operations per second;
- median and 95th-percentile elapsed time;
- allocated bytes and collection counts;
- number of activity CLR activations;
- number of activity child scopes;
- sync and async disposal counts;
- scoped-service identity observed by each activity attempt;
- behavior across retry and resumption; and
- any correctness or isolation failure.

The benchmark environment, warm-up, sample count, runtime version, dependency-registration graph, and
raw results are retained so another maintainer can reproduce the comparison.

## Semantic gates

A strategy is ineligible regardless of throughput when any of the following is true:

- a scoped or disposable service survives longer than the strategy's documented contract;
- two activities observe shared scoped state when the contract promises isolation;
- a retry or resumption reuses an activity CLR object or activation-only service unexpectedly;
- a conditional strategy cannot account for transitive dependencies;
- service location can bypass the strategy's proof; or
- an engine-intrinsic operation creates a CLR activity activation or activity child scope.

## Decision output

The review records:

- the selected strategy and observable lifetime contract;
- performance deltas against the burst-only baseline;
- isolation and disposal consequences;
- rejected strategies and evidence;
- any activity-author opt-in or prohibition; and
- whether ADR 0045 is amended or a focused follow-up ADR supersedes its decision gate.
