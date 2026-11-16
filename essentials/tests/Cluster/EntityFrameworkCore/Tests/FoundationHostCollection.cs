namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// Every test class that boots the built host shares one <see cref="FoundationHostFeed"/>. Packing the fixtures runs
/// <c>dotnet pack --no-build</c> over the same project files into shared obj intermediates, which two packs running at once
/// (one per class fixture, as xunit runs classes in parallel) can corrupt; a collection fixture packs once and serialises
/// the classes that use it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class FoundationHostCollection : ICollectionFixture<FoundationHostFeed>
{
    public const string Name = "foundation-host";
}
