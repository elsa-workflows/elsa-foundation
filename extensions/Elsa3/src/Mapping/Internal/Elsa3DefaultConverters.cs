using Elsa3.Mapping.Conversion;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Internal;

/// <summary>
/// The feature's default converter registrations, as shared factory instances, so
/// <see cref="Elsa3MappingCompositionCheck"/> can tell a default registration from a host's.
/// </summary>
internal static class Elsa3DefaultConverters
{
    public static readonly Func<IServiceProvider, IElsa3DefinitionConverter> DefinitionConverter =
        provider => provider.GetRequiredService<Elsa3DefinitionConverter>();

    /// <summary>
    /// The node converter follows whichever definition converter is composed, so the two cannot disagree. A host whose
    /// definition converter does not also convert nodes registers its own node converter too.
    /// </summary>
    public static readonly Func<IServiceProvider, IElsa3NodeConverter> NodeConverter =
        provider => provider.GetRequiredService<IElsa3DefinitionConverter>() as IElsa3NodeConverter
                    ?? throw new InvalidOperationException(
                        $"The composed {nameof(IElsa3DefinitionConverter)} does not implement {nameof(IElsa3NodeConverter)}. " +
                        $"Register an {nameof(IElsa3NodeConverter)} alongside it, so node and definition conversion follow one policy.");

}
