using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// One section of a container's configuration, read the way the EF persistence's timing and sizing settings are: a blank
/// value leaves the default, and a value that does not parse is refused, naming the key, rather than defaulted.
/// </summary>
internal sealed class EfSettingsSection
{
    private readonly IConfigurationSection _section;
    private readonly string _name;

    private EfSettingsSection(IConfigurationSection section, string name) => (_section, _name) = (section, name);

    /// <summary>Section <paramref name="name"/> of the container's configuration, or null when the container has none.</summary>
    public static EfSettingsSection? Of(IServiceProvider services, string name) =>
        services.GetService<IConfiguration>()?.GetSection(name) is { } section ? new EfSettingsSection(section, name) : null;

    public System.TimeSpan? TimeSpan(string key) =>
        Read<System.TimeSpan>(key, value => System.TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null, "a time span such as '00:00:30'");

    public int? WholeNumber(string key) =>
        Read<int>(key, value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null, "a whole number");

    private T? Read<T>(string key, Func<string, T?> parse, string expected) where T : struct
    {
        var value = _section[key];
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return parse(value) ?? throw new InvalidOperationException($"Configuration '{_name}:{key}' is '{value}', which is not {expected}.");
    }
}
