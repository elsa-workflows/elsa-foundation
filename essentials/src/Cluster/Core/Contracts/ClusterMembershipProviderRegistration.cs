using Elsa.Cluster.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// Names a membership provider and the <see cref="IClusterMembership"/> registration it owns. A host records the
/// provider it selected with one of these, through
/// <see cref="Extensions.ClusterMembershipServiceCollectionExtensions"/>, so a second provider is refused whatever
/// order the two are composed in, and a startup check can name both.
/// </summary>
public sealed class ClusterMembershipProviderRegistration
{
    public ClusterMembershipProviderRegistration(string name, ClusterProviderKind kind, ServiceDescriptor membershipDescriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(membershipDescriptor);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown membership provider kind.");
        if (membershipDescriptor.ServiceType != typeof(IClusterMembership))
            throw new ArgumentException($"The owned descriptor must register {nameof(IClusterMembership)}.", nameof(membershipDescriptor));

        Name = name;
        Kind = kind;
        MembershipDescriptor = membershipDescriptor;
    }

    /// <summary>The provider's name, as startup diagnostics report it.</summary>
    public string Name { get; }

    /// <summary>In-process for the default cluster of one; durable for every provider a host opts into.</summary>
    public ClusterProviderKind Kind { get; }

    /// <summary>The <see cref="IClusterMembership"/> registration this provider owns.</summary>
    public ServiceDescriptor MembershipDescriptor { get; }
}
