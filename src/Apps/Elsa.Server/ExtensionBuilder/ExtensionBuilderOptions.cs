namespace Elsa.Server.ExtensionBuilder;

internal sealed class ExtensionBuilderOptions
{
    public string StoragePath { get; set; } = "extension-builder";
    public string GitExecutable { get; set; } = "git";
    public string[] ServerLocalRepositoryRoots { get; set; } = [];
    public string DotNetExecutable { get; set; } = "";
    public string[] TrustedRoles { get; set; } = ["Admin", "Administrator", "Trusted", "ExtensionBuilder"];
    public string[] DeniedDependencyPatterns { get; set; } = [];
}
