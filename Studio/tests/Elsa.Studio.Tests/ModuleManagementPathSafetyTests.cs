using Elsa.Studio.Web;

namespace Elsa.Studio.Tests;

/// <summary>
/// Path-containment guards for the drop folder (<see cref="ElsaModuleManagementApi.EnsureChildPath"/>) and the
/// package static-asset server (<see cref="NuplaneStaticWebAssetsEndpoint.IsChildPath"/>). These reject directory
/// traversal so a caller cannot read or overwrite files outside the intended roots.
/// </summary>
public sealed class ModuleManagementPathSafetyTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "elsa-drop-folder");

    [Theory]
    [InlineData("../evil.nupkg")]
    [InlineData("../../evil.nupkg")]
    [InlineData("nested/../../evil.nupkg")]
    public void EnsureChildPathRejectsRelativeTraversal(string fileName)
    {
        Assert.Throws<InvalidOperationException>(() => ElsaModuleManagementApi.EnsureChildPath(Root, fileName));
    }

    [Fact]
    public void EnsureChildPathRejectsAbsolutePath()
    {
        var absolute = OperatingSystem.IsWindows() ? @"C:\Windows\evil.nupkg" : "/etc/evil.nupkg";

        Assert.Throws<InvalidOperationException>(() => ElsaModuleManagementApi.EnsureChildPath(Root, absolute));
    }

    [Fact]
    public void EnsureChildPathAllowsSimpleFileName()
    {
        var resolved = ElsaModuleManagementApi.EnsureChildPath(Root, "package.nupkg");

        Assert.Equal(Path.Combine(Path.GetFullPath(Root), "package.nupkg"), resolved);
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("../../secrets")]
    [InlineData("../staticwebassets-sibling/asset.css")]
    public void IsChildPathRejectsPathsOutsideParent(string relative)
    {
        var parent = Path.Combine(Root, "staticwebassets");
        var candidate = Path.GetFullPath(Path.Combine(parent, relative));

        Assert.False(NuplaneStaticWebAssetsEndpoint.IsChildPath(Path.GetFullPath(parent), candidate));
    }

    [Fact]
    public void IsChildPathRejectsParentItself()
    {
        var parent = Path.GetFullPath(Path.Combine(Root, "staticwebassets"));

        Assert.False(NuplaneStaticWebAssetsEndpoint.IsChildPath(parent, parent));
    }

    [Fact]
    public void IsChildPathAllowsNestedAsset()
    {
        var parent = Path.GetFullPath(Path.Combine(Root, "staticwebassets"));
        var asset = Path.Combine(parent, "css", "site.css");

        Assert.True(NuplaneStaticWebAssetsEndpoint.IsChildPath(parent, asset));
    }
}
