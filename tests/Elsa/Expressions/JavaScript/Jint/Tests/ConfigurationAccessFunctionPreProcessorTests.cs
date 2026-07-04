using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Options;
using Elsa.Expressions.JavaScript.PreProcessors;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Issue #375: the <c>getConfiguration</c> blocklist must be case-insensitive and hierarchy-aware so
/// a disallowed section cannot be bypassed by requesting a child key or a differently-cased name.
/// </summary>
public class ConfigurationAccessFunctionPreProcessorTests
{
    private static Func<string, object?> CaptureGetConfiguration(IEnumerable<string> disallowedSections, IEnumerable<KeyValuePair<string, string?>> values)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ConfigurationAccessFunctionProviderOptions
        {
            AllowConfigurationAccess = true,
            DisallowedSections = disallowedSections
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var preProcessor = new ConfigurationAccessFunctionPreProcessor(options, configuration);
        var capture = new FunctionCapturingExecutionContext();

        preProcessor.PreProcess("", capture, null!, null, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        var function = Assert.IsAssignableFrom<JavaScriptFunction>(capture.Functions.Single(f => f.Name == FunctionNames.GetConfiguration));
        return (Func<string, object?>)function.Delegate;
    }

    [Fact]
    public void ChildKeyOfDisallowedSectionIsBlocked()
    {
        var getConfiguration = CaptureGetConfiguration(
            ["ConnectionStrings"],
            [new("ConnectionStrings:DefaultConnection", "Server=secret;")]);

        Assert.Throws<ArgumentException>(() => getConfiguration("ConnectionStrings:DefaultConnection"));
    }

    [Fact]
    public void DifferentlyCasedDisallowedSectionIsBlocked()
    {
        var getConfiguration = CaptureGetConfiguration(
            ["ConnectionStrings"],
            [new("connectionstrings:DefaultConnection", "Server=secret;")]);

        Assert.Throws<ArgumentException>(() => getConfiguration("connectionstrings"));
        Assert.Throws<ArgumentException>(() => getConfiguration("connectionstrings:defaultconnection"));
    }

    [Fact]
    public void ExactDisallowedSectionIsBlocked()
    {
        var getConfiguration = CaptureGetConfiguration(
            ["ConnectionStrings"],
            [new("ConnectionStrings", "value")]);

        Assert.Throws<ArgumentException>(() => getConfiguration("ConnectionStrings"));
    }

    [Fact]
    public void UnrelatedKeyIsAllowed()
    {
        var getConfiguration = CaptureGetConfiguration(
            ["ConnectionStrings"],
            [new("Feature:Enabled", "true")]);

        Assert.Equal("true", getConfiguration("Feature:Enabled"));
    }

    [Fact]
    public void KeyMerelyPrefixedByDisallowedNameIsAllowed()
    {
        // "ConnectionStringsExtra" is not a child of "ConnectionStrings" (no ':' boundary), so it stays allowed.
        var getConfiguration = CaptureGetConfiguration(
            ["ConnectionStrings"],
            [new("ConnectionStringsExtra", "ok")]);

        Assert.Equal("ok", getConfiguration("ConnectionStringsExtra"));
    }

    private sealed class FunctionCapturingExecutionContext : IJavaScriptExecutionContext
    {
        public List<IJavaScriptFunction> Functions { get; } = [];

        public void RegisterFunction(IJavaScriptFunction function) => Functions.Add(function);

        public object? GetValue(string name) => null;
        public void SetValue(string name, object? value) { }
        public void RegisterType(Type type) { }
        public object? NormalizeValue(object? value) => value;
        public object? Evaluate(string script) => null;
        public void Execute(string script) { }
    }
}
