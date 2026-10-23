using System.IO.Compression;
using System.Text;
using Elsa.Studio.Web;

namespace Elsa.Studio.Tests;

/// <summary>
/// Validation of uploaded packages before they can reach the drop folder and be loaded by reconciliation:
/// the file name must be a bare <c>.nupkg</c>, and the bytes must be a readable ZIP archive (the NuGet container).
/// </summary>
public sealed class ModuleManagementUploadValidationTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nupkg");

    [Theory]
    [InlineData("package.zip")]
    [InlineData("package.dll")]
    [InlineData("package")]
    public void RejectsNonNupkgExtension(string fileName)
    {
        Assert.Equal("Only .nupkg files can be uploaded.", ElsaModuleManagementApi.ValidateUploadFileName(fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../evil.nupkg")]
    [InlineData("nested/evil.nupkg")]
    public void RejectsMissingOrPathQualifiedFileName(string fileName)
    {
        Assert.Equal("A .nupkg file name is required.", ElsaModuleManagementApi.ValidateUploadFileName(fileName));
    }

    [Fact]
    public void AcceptsBareNupkgFileName()
    {
        Assert.Null(ElsaModuleManagementApi.ValidateUploadFileName("Elsa.Some.Package.1.0.0.nupkg"));
    }

    [Fact]
    public void RejectsMalformedNupkgArchive()
    {
        File.WriteAllText(_tempFile, "this is not a zip archive");

        Assert.Equal("The uploaded file is not a valid .nupkg package.", ElsaModuleManagementApi.ValidateNupkgArchive(_tempFile));
    }

    [Fact]
    public void AcceptsWellFormedZipArchive()
    {
        using (var archive = ZipFile.Open(_tempFile, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("package.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("<package/>");
        }

        Assert.Null(ElsaModuleManagementApi.ValidateNupkgArchive(_tempFile));
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
