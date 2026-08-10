using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// Opens one Groundwork target of a single provider kind. Provider packages contribute a leaf; the
/// provider-neutral target registry never learns what any provider's options mean.
/// </summary>
public interface IGroundworkProviderLeaf
{
    /// <summary>The identity a target names to select this leaf, matched case-insensitively.</summary>
    string ProviderIdentity { get; }

    /// <summary>
    /// Declares <paramref name="targetName"/> against this provider, binding <paramref name="entry"/>'s
    /// opaque options to the leaf's own typed settings.
    /// </summary>
    void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry);
}

/// <summary>
/// Marries host-configured targets to the provider leaves that can open them, in either composition order.
/// <para>
/// A shell feature graph gives no guarantee that the provider feature configures before the feature holding
/// the target list, so neither side may assume the other has run. Both sides deposit into this mailbox and
/// it applies every request whose provider is present; a request naming a provider the host never composed
/// stays pending and is reported at readiness rather than silently doing nothing.
/// </para>
/// </summary>
public sealed class GroundworkTargetComposition
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, GroundworkTargetEntry> requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IGroundworkProviderLeaf> leaves = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> applied = new(StringComparer.Ordinal);

    /// <summary>Target names a host asked for that no composed provider leaf could open.</summary>
    public IReadOnlyList<string> PendingTargets
    {
        get
        {
            lock (gate)
                return requests.Keys.Except(applied, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>The provider a pending target asked for, used to name the missing package in a diagnostic.</summary>
    public string? RequestedProvider(string targetName)
    {
        lock (gate)
            return requests.GetValueOrDefault(GroundworkTargetNames.Normalize(targetName))?.Provider;
    }

    /// <summary>The provider identities of every composed leaf, in ordinal order.</summary>
    public IReadOnlyList<string> ComposedProviders
    {
        get
        {
            lock (gate)
                return leaves.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>Records a host-configured target, opening it immediately when its provider is present.</summary>
    public void RequestTarget(IServiceCollection services, string? targetName, GroundworkTargetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        var target = GroundworkTargetNames.Normalize(targetName);
        if (string.IsNullOrWhiteSpace(entry.Provider))
        {
            throw new GroundworkTargetConfigurationException(
                $"Groundwork target '{target}' does not name a provider. Set 'Provider' to a composed " +
                "Groundwork provider leaf, for example 'sqlite'.");
        }

        IGroundworkProviderLeaf? leaf;
        lock (gate)
        {
            if (requests.TryGetValue(target, out var existing) && !ReferenceEquals(existing, entry))
            {
                throw new GroundworkTargetConflictException(
                    $"Groundwork target '{target}' is configured more than once. Give each physical store " +
                    "its own target name.");
            }

            requests[target] = entry;
            leaf = leaves.GetValueOrDefault(entry.Provider);
            if (leaf is null)
                return;

            applied.Add(target);
        }

        leaf.DeclareTarget(services, target, entry);
    }

    /// <summary>Composes a provider leaf, opening every already-requested target it can serve.</summary>
    public void AddProviderLeaf(IServiceCollection services, IGroundworkProviderLeaf leaf)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(leaf);
        List<KeyValuePair<string, GroundworkTargetEntry>> pending;
        lock (gate)
        {
            if (leaves.TryGetValue(leaf.ProviderIdentity, out var existing))
            {
                if (existing.GetType() == leaf.GetType())
                    return;

                throw new GroundworkTargetConflictException(
                    $"Two Groundwork provider leaves claim the identity '{leaf.ProviderIdentity}': " +
                    $"'{existing.GetType().FullName}' and '{leaf.GetType().FullName}'.");
            }

            leaves.Add(leaf.ProviderIdentity, leaf);
            pending = requests
                .Where(request =>
                    !applied.Contains(request.Key) &&
                    string.Equals(request.Value.Provider, leaf.ProviderIdentity, StringComparison.OrdinalIgnoreCase))
                .OrderBy(request => request.Key, StringComparer.Ordinal)
                .ToList();
            foreach (var request in pending)
                applied.Add(request.Key);
        }

        foreach (var (target, entry) in pending)
            leaf.DeclareTarget(services, target, entry);
    }
}
