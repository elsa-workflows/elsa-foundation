using CShells.Configuration;
using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Composes host defaults without constructing features or activating a shell.</summary>
public interface IEfToolingShellDefaults
{
    void Configure(ShellBuilder builder, IConfiguration configuration);
}
