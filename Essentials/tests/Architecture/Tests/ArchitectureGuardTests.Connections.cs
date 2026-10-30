using System.Reflection;
using Elsa.Activities.Http.Activities;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
{
    [Fact]
    public void Connections_core_has_no_azure_or_valence_types()
    {
        var assembly = typeof(IConnectionResolver).Assembly;
        var names = assembly.GetExportedTypes().Select(type => type.FullName ?? type.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Azure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Valence", StringComparison.OrdinalIgnoreCase));

        var sourceFiles = Directory.EnumerateFiles(
                Path.Combine(RepoRoot, "Essentials", "src", "Connections"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Azure.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Valence", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SendHttpRequest_has_no_Authorization_input()
    {
        var authorization = typeof(SendHttpRequest).GetProperty("Authorization", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(authorization);
        Assert.NotNull(typeof(SendHttpRequest).GetProperty("Connection", BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void Authentication_schemes_are_an_open_plugin_contract()
    {
        Assert.True(typeof(IAuthenticationSchemeProvider).IsInterface);
        Assert.True(typeof(IHttpRequestAuthenticator).IsInterface);
        Assert.True(typeof(IAccessTokenSource).IsInterface);
        Assert.Contains(HttpAuthSchemes.ApiKey, new[] { HttpAuthSchemes.ApiKey, HttpAuthSchemes.Basic, HttpAuthSchemes.Bearer });
        Assert.False(typeof(HttpAuthSchemes).IsEnum);
    }
}
