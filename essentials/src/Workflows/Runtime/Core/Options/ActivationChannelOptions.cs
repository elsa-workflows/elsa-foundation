using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Options;

/// <summary>Shell-wide declarations and policy for workflow activation channels.</summary>
/// <remarks>
/// Channel names match activation slot names exactly with <see cref="StringComparer.Ordinal"/>. Comparisons are
/// case-sensitive and preserve configured values without trimming or normalization. Slot identity remains scoped to a
/// workflow definition; this options object only declares which names a shell accepts.
/// </remarks>
public sealed class ActivationChannelOptions
{
    /// <summary>Whether activation may create a slot whose name is not in <see cref="Channels"/>.</summary>
    /// <remarks>Defaults to <see langword="true"/> to preserve existing runtime compositions.</remarks>
    public bool AllowAdHocChannels { get; set; } = true;

    /// <summary>Named channels accepted by this shell.</summary>
    public List<ActivationChannelDefinition> Channels { get; set; } = [];

    /// <summary>Whether the shell permits an activation using the exact supplied channel name.</summary>
    public bool Allows(string channelName) =>
        AllowAdHocChannels || Channels?.Any(channel => channel is not null && StringComparer.Ordinal.Equals(channel.Name, channelName)) == true;
}
