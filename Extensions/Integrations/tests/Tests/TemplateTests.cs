using System.Diagnostics;
using System.Text.Json;

namespace Elsa.Integrations.Tests;

public sealed class TemplateTests
{
    [Fact]
    public void Dotnet_new_template_is_elsa_connector()
    {
        var templateRoot = TemplateRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(templateRoot, ".template.config", "template.json")));
        Assert.Equal("elsa-connector", document.RootElement.GetProperty("shortName").GetString());
        Assert.Equal("Elsa.Integrations.Connector", document.RootElement.GetProperty("identity").GetString());
        Assert.Equal("Company.Connector", document.RootElement.GetProperty("sourceName").GetString());

        var outputs = document.RootElement.GetProperty("primaryOutputs").EnumerateArray()
            .Select(item => item.GetProperty("path").GetString())
            .ToArray();
        Assert.Contains("src/Company.Connector/Company.Connector.csproj", outputs);
        Assert.Contains("tests/Company.Connector.Tests/Company.Connector.Tests.csproj", outputs);

        var project = File.ReadAllText(Path.Combine(templateRoot, "src", "Company.Connector", "Company.Connector.csproj"));
        Assert.Contains("Version=\"4.0.*-*\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Integrations.Sdk.Testing", project, StringComparison.Ordinal);

        var tests = File.ReadAllText(Path.Combine(templateRoot, "tests", "Company.Connector.Tests", "Company.Connector.Tests.csproj"));
        Assert.Contains("Elsa.Integrations.Sdk.Testing", tests, StringComparison.Ordinal);
        Assert.Contains("Version=\"4.0.*-*\"", tests, StringComparison.Ordinal);
        Assert.Contains(@"..\..\src\Company.Connector\Company.Connector.csproj", tests, StringComparison.Ordinal);
    }

    [Fact]
    public void Connector_project_does_not_compile_tests_sources()
    {
        var templateRoot = TemplateRoot();
        var connectorProject = Path.Combine(templateRoot, "src", "Company.Connector", "Company.Connector.csproj");
        var testsDirectory = Path.Combine(templateRoot, "tests", "Company.Connector.Tests");
        Assert.True(File.Exists(connectorProject), connectorProject);
        Assert.True(Directory.Exists(testsDirectory), testsDirectory);
        Assert.False(
            Directory.Exists(Path.Combine(templateRoot, "src", "Company.Connector", "Tests")),
            "Tests must be a sibling of src/Company.Connector, not nested inside it — the SDK Compile glob would otherwise include PingTests.cs in the connector package.");

        var scratch = Directory.CreateTempSubdirectory(nameof(TemplateTests));
        try
        {
            var copiedRoot = Path.Combine(scratch.FullName, "elsa-connector");
            CopyDirectory(templateRoot, copiedRoot);
            var copiedConnector = Path.Combine(copiedRoot, "src", "Company.Connector", "Company.Connector.csproj");
            var copiedTestsDirectory = Path.GetFullPath(Path.Combine(copiedRoot, "tests", "Company.Connector.Tests"));
            var result = Path.Combine(scratch.FullName, "compile.json");

            var (exitCode, output) = Dotnet(
            [
                "msbuild", copiedConnector, "-getItem:Compile", $"-getResultOutputFile:{result}",
                "-nologo", "-restore:false"
            ]);
            Assert.True(exitCode == 0 && File.Exists(result), $"evaluating Compile items failed with exit {exitCode}:\n{output}");

            using var items = JsonDocument.Parse(File.ReadAllText(result));
            var compile = items.RootElement.GetProperty("Items").GetProperty("Compile");
            var paths = compile.EnumerateArray()
                .Select(item => Path.GetFullPath(
                    item.TryGetProperty("FullPath", out var fullPath)
                        ? fullPath.GetString() ?? string.Empty
                        : item.GetProperty("Identity").GetString() ?? string.Empty))
                .ToArray();

            Assert.Contains(paths, path => path.Replace('\\', '/').EndsWith("/Activities/Ping.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(paths, path =>
                path.StartsWith(copiedTestsDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || path.EndsWith("PingTests.cs", StringComparison.Ordinal));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static string TemplateRoot() =>
        Path.GetFullPath(Path.Combine(RepoRoot(), "Extensions", "Integrations", "templates", "elsa-connector"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj")
                continue;
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    /// <summary>
    /// Runs the <c>dotnet</c> this test run was launched with. Strips inherited MSBuild variables so a spawned
    /// <c>dotnet msbuild</c> resolves its own SDK instead of the test host's.
    /// </summary>
    private static (int ExitCode, string Output) Dotnet(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("MSBuildSDKsPath");

        using var process = Process.Start(startInfo)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(TimeSpan.FromMinutes(2));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        var output = stdOutTask.GetAwaiter().GetResult() + stdErrTask.GetAwaiter().GetResult();
        if (!exited)
            throw new TimeoutException($"dotnet {string.Join(' ', startInfo.ArgumentList)} did not exit within two minutes. Output so far:\n{output}");

        return (process.ExitCode, output);
    }
}
