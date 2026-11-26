namespace Elsa.Studio.Hosting;

/// <summary>Studio module discovery could not admit a bounded package read.</summary>
internal sealed class StudioModuleDiscoveryBusyException(Exception? innerException = null)
    : Exception("Studio module discovery is temporarily busy.", innerException);
