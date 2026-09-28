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
- **Durable provider:** `EfClusterMembership` (`Elsa.Cluster.EntityFrameworkCore`, spec 183 B2), one shared table on SQLite, SQL Server, PostgreSQL or MySQL under its own `[EfModule("Cluster.Membership")]`. A host composes it from configuration with `AddConfiguredClusterMembership(configuration)` (namespace `Elsa.Cluster.Hosting`), which registers nothing unless `Elsa:Cluster:Membership:EntityFrameworkCore:Enabled` is `true`; its store settings (`Provider`, `ConnectionString`, `ConnectionName`, `Schema`, `Pooling`, `CleanupPeriod`) sit beside that switch, and the host id and timings under `Elsa:Cluster:Membership`. `AddEfClusterMembership(options)` composes it in code. It migrates through the plain-host migrator only, joins while the host starts, heartbeats on a fixed interval, drains as the host stops and leaves once it has.
- **Conformance:** a provider is supported only once it passes `ClusterMembershipConformanceTests` (`tests/essentials/Cluster/Testing`), supplying an `IClusterMembershipConformanceFixture`.

---

## Implementable contributor interfaces

### `IMemberReportSource<TSection>` *(Core — `Elsa.Cluster.Core`)*
- **Kind:** Source. Returns one section of this host's member report; exactly one source per section.
- **Signature:** `ValueTask<TSection> ReadAsync(CancellationToken ct)`, where `TSection` is a `MemberReportSection` the contract defines. Today that is `ReadabilitySection`.
- **Register:** as `IMemberReportSource<TSection>` on the host container, beside the provider, so every shell publishes the same report.
- **Consumed by:** every provider, through `MemberReportComposition`, each time it publishes. Two sources for one section refuse the provider when it is constructed.
- **Known implementations:** none yet. The readability source arrives with B3 ([#2099](https://github.com/elsa-workflows/elsa-foundation/issues/2099)).

---

## Cross-references

- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.2 + §2.22.1.
