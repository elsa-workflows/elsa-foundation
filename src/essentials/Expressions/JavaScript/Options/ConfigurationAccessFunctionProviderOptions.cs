namespace Elsa.Expressions.JavaScript.Options;

public class ConfigurationAccessFunctionProviderOptions
{
    public bool AllowConfigurationAccess { get; set; } = false;

    public IEnumerable<string> DisallowedSections { get; set; } = [];
}