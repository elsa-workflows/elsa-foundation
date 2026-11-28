using System.Reflection;

namespace Elsa.Studio.Api.Contracts;

internal interface IStudioPackageEntryStyles
{
    /// <summary>Reads the stylesheet contract from the loaded module's package, if present.</summary>
    ValueTask<string[]?> ReadAsync(Assembly assembly, string directory, CancellationToken cancellationToken);
}
