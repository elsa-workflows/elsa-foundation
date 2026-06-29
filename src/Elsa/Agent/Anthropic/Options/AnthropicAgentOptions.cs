namespace Elsa.Agent.Anthropic.Options;

public sealed class AnthropicAgentOptions
{
    public const string SectionName = "Elsa:Agent:Anthropic";

    /// <summary>Whether the Anthropic provider is active. Opt-in; the single-harness rule means enabling this excludes other harnesses.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Anthropic API key. When unset, <see cref="ApiKeyEnvironmentVariable"/> is consulted.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The environment variable read for the API key when <see cref="ApiKey"/> is not configured directly.</summary>
    public string? ApiKeyEnvironmentVariable { get; set; } = "ANTHROPIC_API_KEY";

    /// <summary>The Claude model id the provider drives (e.g. <c>claude-opus-4-8</c>). Configure per deployment.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Optional system prompt prepended to every turn.</summary>
    public string? SystemMessage { get; set; }

    /// <summary>The maximum number of output tokens to request per step.</summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>When true, content marked <c>Sensitive</c> is included in the prompt; otherwise only <c>Internal</c> and below.</summary>
    public bool IncludeSensitiveContextContent { get; set; }
}
