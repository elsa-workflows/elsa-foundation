namespace Elsa.Modularity.ExtensionBuilder;

/// <summary>
/// Stateless rendering of Extension Builder repository templates: resolves and validates the template
/// parameter values, computes the safe target path, and renders each template file's path and content
/// (including the special-cased .csproj and elsa-package.json files). Repository path safety is
/// delegated to <see cref="RepositoryFileSystem.NormalizeRelativePath"/>.
/// </summary>
internal static class RepositoryTemplateRenderer
{
    public static IReadOnlyDictionary<string, string> BuildParameterValues(ExtensionTemplate template, ApplyRepositoryTemplateRequest request)
    {
        var provided = request.Parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.Parameters, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["packageId"] = template.DefaultPackageId,
            ["packageVersion"] = template.DefaultPackageVersion,
            ["targetFramework"] = template.DefaultTargetFramework
        };

        foreach (var parameter in template.Parameters)
        {
            provided.TryGetValue(parameter.Name, out var providedValue);
            var value = string.IsNullOrWhiteSpace(providedValue) ? parameter.DefaultValue : providedValue.Trim();
            if (parameter.Required && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Template parameter '{parameter.Name}' is required.", nameof(request));

            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateParameter(parameter.Name, value);
                values[parameter.Name] = value;
            }
        }

        return values;
    }

    public static string NormalizeTargetPath(string? targetPath, ExtensionTemplateScope scope, IReadOnlyDictionary<string, string> values)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
            return RepositoryFileSystem.NormalizeRelativePath(targetPath);

        return scope switch
        {
            ExtensionTemplateScope.Project => values.TryGetValue("name", out var name) ? RepositoryFileSystem.NormalizeRelativePath(name) : "",
            ExtensionTemplateScope.Item => "src",
            _ => ""
        };
    }

    public static string CombinePath(string targetPath, string templatePath) =>
        string.IsNullOrWhiteSpace(targetPath)
            ? templatePath
            : $"{targetPath.TrimEnd('/')}/{templatePath.TrimStart('/')}";

    public static string RenderContent(ExtensionTemplate template, ProjectTemplateFile file, string renderedPath, IReadOnlyDictionary<string, string> values)
    {
        var packageId = values.TryGetValue("packageId", out var configuredPackageId) ? configuredPackageId : template.DefaultPackageId;
        var packageVersion = values.TryGetValue("packageVersion", out var configuredPackageVersion) ? configuredPackageVersion : template.DefaultPackageVersion;
        var targetFramework = values.TryGetValue("targetFramework", out var configuredTargetFramework) ? configuredTargetFramework : template.DefaultTargetFramework;
        if (renderedPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return ExtensionBuilderTemplateCatalog.ProjectFile(packageId, packageVersion, targetFramework);
        if (Path.GetFileName(renderedPath).Equals("elsa-package.json", StringComparison.OrdinalIgnoreCase))
            return ExtensionBuilderTemplateCatalog.RewriteManifest(template.DefaultManifest.Content, packageId, packageVersion).GetRawText();
        return RenderText(file.Content, values);
    }

    public static string RenderText(string text, IReadOnlyDictionary<string, string> values)
    {
        var rendered = text;
        foreach (var (key, value) in values)
            rendered = rendered.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);
        return rendered;
    }

    private static void ValidateParameter(string name, string value)
    {
        if (name.Equals("name", StringComparison.OrdinalIgnoreCase) && !IsSafeIdentifier(value))
            throw new ArgumentException("Template parameter 'name' must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.", nameof(value));
        if ((name.Equals("namespace", StringComparison.OrdinalIgnoreCase) || name.Equals("packageId", StringComparison.OrdinalIgnoreCase)) && !IsSafeIdentifier(value))
            throw new ArgumentException($"Template parameter '{name}' must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.", nameof(value));
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length > 0 && char.IsLetter(value[0]) && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
}
