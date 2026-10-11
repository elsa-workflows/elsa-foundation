using Elsa.Cluster.Testing;
using Elsa.Cluster.Tests.InProcess;
using Elsa.Cluster.Tests.Reference;

namespace Elsa.Cluster.Tests;

/// <summary>The in-process default against the conformance suite: every single-member test passes, and every
/// multi-member test reports itself not applicable to a cluster of one (spec 183, User Story 5).</summary>
public sealed class InProcessClusterMembershipConformanceTests() : ClusterMembershipConformanceTests(new InProcessConformanceFixture());

/// <summary>The reference durable provider against the whole suite, proving the multi-member tests pass for a correct
/// provider; <see cref="BrokenProviderBiteTests"/> proves they fail for broken ones.</summary>
public sealed class SharedStoreClusterMembershipConformanceTests() : ClusterMembershipConformanceTests(new SharedStoreConformanceFixture());
