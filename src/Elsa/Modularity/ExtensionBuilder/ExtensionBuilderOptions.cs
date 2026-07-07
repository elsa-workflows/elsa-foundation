namespace Elsa.Modularity.ExtensionBuilder;

internal sealed class ExtensionBuilderOptions
{
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Elsa",
        "ExtensionBuilder");
    public string GitExecutable { get; set; } = "git";
    public string[] ServerLocalRepositoryRoots { get; set; } = [];
    public string DotNetExecutable { get; set; } = "";
    public string[] TrustedRoles { get; set; } = ["Admin", "Administrator", "Trusted", "ExtensionBuilder"];
    public string[] DeniedDependencyPatterns { get; set; } = [];
}
