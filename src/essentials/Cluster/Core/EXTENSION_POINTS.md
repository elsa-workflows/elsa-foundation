# Extension points — Cluster domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Cluster.Core`, the cluster membership contract of
[ADR 0078](../../../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)
and spec 183. The domain has no feature project: the in-process default is the helper library `Elsa.Cluster.InProcess`,
and a durable provider is composed once on the host container, never per shell.

---

## Overridable contracts

### `IClusterMembership` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Replacement contract (§2.6.2), declared by `[ClusterMembershipReplacementContract]`. One provider is active per host process.
- **Signature:** `ProviderKind`; `GetLocalStanding()`; `ReadFleetAsync(FleetReadMode, ct)`; `PublishReportAsync(ct)`; `QueryAsync(MemberQuery, FleetReadMode, ct)`.
- **Default impl:** `InProcessClusterMembership` (`Elsa.Cluster.InProcess`), a cluster of one that writes nothing durable and runs nothing in the background. Consumers register it with `TryAddInProcessClusterMembership()`, so a host composes nothing to get it.
- **Override:** a durable provider registers through `AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(name, ClusterProviderKind.Durable, descriptor))` on the host container. The in-process default yields to it in either order; a second durable provider, or an `IClusterMembership` registered directly, fails at startup with a diagnostic naming both. A durable provider starts only with an explicit `Elsa:Cluster:Membership:HostId`.
- **Conformance:** a provider is supported only once it passes `ClusterMembershipConformanceTests` (`tests/essentials/Cluster/Testing`), supplying an `IClusterMembershipConformanceFixture`.

---

## Implementable contributor interfaces

### `IMemberReportSource<TSection>` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Source. Returns one section of this host's member report; exactly one source per section.
- **Signature:** `ValueTask<TSection> ReadAsync(CancellationToken ct)`, where `TSection` is a `MemberReportSection` the contract defines: `ReadabilitySection` (spec 183) and `RunnabilitySection` (spec 184).
- **Register:** as `IMemberReportSource<TSection>` on the host container, beside the provider, so every shell publishes the same report.
- **Consumed by:** every provider, through `MemberReportComposition`, each time it publishes. Two sources for one section refuse the provider when it is constructed.
- **Requirement kinds:** a member query's requirements come from a closed vocabulary: `ReadsSchemaVersion` (spec 183), and `ActivatesRuntimeConsumer`, `HasStorageDriver` and `ResolvesActivityType` (spec 184, FR-006). The three runnability kinds are met jointly, by one runnability entry that applies to each requirement's database (FR-009).
- **Known implementations:** `WorkflowRuntimeRunnabilitySource` (`Elsa.Workflows.Runtime.Distributed`, B7, [#2103](https://github.com/elsa-workflows/elsa-foundation/issues/2103)) for `RunnabilitySection`: one entry per shell whose distributed runtime is active, derived from exactly the registries that shell's `IRuntimeRequirementChecker` reads. A host composes it with `AddWorkflowRuntimeRunnabilityReport()`, beside its membership provider, so every shell records its entry in one host-level registry. `EfSchemaReadabilitySource` (`Elsa.Cluster.Readability`, B3, [#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)) for `ReadabilitySection`: one entry per schema family whose `[EfSchemaFamily]` declaration is loaded in the process, from any load context, at the versions every loaded declaration of it reads. A host composes it with `AddEfSchemaReadability()`, which also registers the in-process default, so an unclustered host reports its readability with nothing else composed.

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
