using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Executables;

internal sealed class ActivationChannelOptionsValidator : IValidateOptions<ActivationChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, ActivationChannelOptions options)
    {
        var failures = new List<string>();
        if (options.Channels is null)
            failures.Add("Activation channel declarations cannot be null.");
        else
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < options.Channels.Count; i++)
            {
                var channel = options.Channels[i];
                if (channel is null)
                {
                    failures.Add($"Activation channel declaration at index {i} cannot be null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(channel.Name))
                    failures.Add($"Activation channel declaration at index {i} must have a non-blank name.");
                else if (!names.Add(channel.Name))
                    failures.Add($"Activation channel name '{channel.Name}' is duplicated using ordinal comparison.");

                if (channel.DisplayName is not null && string.IsNullOrWhiteSpace(channel.DisplayName))
                    failures.Add($"Activation channel '{channel.Name}' must have a non-blank display name when one is specified.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
