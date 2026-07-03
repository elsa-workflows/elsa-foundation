using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Constraints;
using Microsoft.Extensions.Options;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

internal sealed class JintEngineFactory(
    IEnumerable<IJintEngineOptionsConfigurator> jintEngineOptionsConfigurators,
    IOptions<FeatureOptions> featureOptions)
    : IJintEngineFactory
{
    // A cancelable-but-never-cancelled placeholder token, used only so a CancellationConstraint is
    // registered on the cached options (Jint registers one only for a token that CanBeCanceled). Each
    // Create() rebinds that constraint to the caller's real token. Static + never disposed by design:
    // holding the token keeps its source alive for the app lifetime.
    private static readonly CancellationToken PlaceholderToken = new CancellationTokenSource().Token;

    private JintOptions? _cachedOptions;

    public ValueTask<Engine> Create(IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
    {
        var jintOptions = GetOrBuildOptions(options);
        var engine = new Engine(jintOptions);

        // DS-9: honour the caller's CancellationToken (previously discarded). The constraint was
        // registered on the cached options with the placeholder token; rebind it for this evaluation so
        // a cancelled token aborts the script. A non-cancelable token leaves the placeholder in place,
        // which never cancels — i.e. no cancellation requested.
        if (cancellationToken.CanBeCanceled)
            engine.Constraints.Find<CancellationConstraint>()?.Reset(cancellationToken);

        return new ValueTask<Engine>(engine);
    }

    private JintOptions GetOrBuildOptions(IExpressionEvaluatorOptions? evaluatorOptions)
    {
        // DS-10: assemble the engine options (the IJintEngineOptionsConfigurator pass + sandbox
        // constraints) once per factory scope and reuse them for every evaluation; only the Engine
        // itself is constructed fresh per call, preserving per-evaluation state isolation (engines
        // retain mutable global state, so they are not pooled/shared).
        //
        // CONTRACT: the shipped configurators do not read `evaluatorOptions`, so caching independent of
        // it is correct. A future configurator that needs per-call options must NOT rely on this cache —
        // JintEngineFactoryTests pins the "configurators run once per scope" behaviour so such a change
        // trips a loud test rather than silently reading stale, first-call options.
        if (_cachedOptions is not null)
            return _cachedOptions;

        var jintOptions = new JintOptions
        {
            ExperimentalFeatures = ExperimentalFeature.TaskInterop
        };

        foreach (var configurator in jintEngineOptionsConfigurators)
            configurator.Configure(jintOptions, evaluatorOptions);

        ApplySandboxConstraints(jintOptions);

        _cachedOptions = jintOptions;
        return jintOptions;
    }

    private void ApplySandboxConstraints(JintOptions jintOptions)
    {
        var options = featureOptions.Value;

        // DS-9: wire the sandbox limits. Each is independently disabled by a null/non-positive value.
        if (options.ExecutionTimeout is { } timeout && timeout > TimeSpan.Zero)
            jintOptions.TimeoutInterval(timeout);

        if (options.MaxStatements is { } maxStatements && maxStatements > 0)
            jintOptions.MaxStatements(maxStatements);

        if (options.MaxRecursionDepth is { } maxRecursionDepth && maxRecursionDepth > 0)
            jintOptions.LimitRecursion(maxRecursionDepth);

        // Register the cancellation constraint so Create() can rebind it to the per-call token.
        jintOptions.CancellationToken(PlaceholderToken);
    }
}