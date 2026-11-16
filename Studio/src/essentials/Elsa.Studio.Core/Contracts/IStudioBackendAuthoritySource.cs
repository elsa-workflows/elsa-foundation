using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Core.Contracts;

/// <summary>Replacement contract: at most one custom source may select the initial host authority.</summary>
/// <remarks>The host copies the result into its own immutable lifetime snapshot. Interactive selection cannot change it.</remarks>
public interface IStudioBackendAuthoritySource
{
    StudioBackendAuthority Resolve(StudioBackendAuthorityContext context);
}

/// <summary>The host-owned authority snapshot, shared by reference with its shell generations.</summary>
public interface IStudioBackendAuthorityAccessor
{
    StudioBackendAuthority Value { get; }
}
