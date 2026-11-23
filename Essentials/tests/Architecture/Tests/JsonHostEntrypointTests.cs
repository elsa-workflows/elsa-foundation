using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHostEntrypointTests
{
    [Theory]
    [InlineData("Elsa.Foundation.Host", true)]
    [InlineData("Elsa.Workbench", true)]
    [InlineData("JsonHostOptionsProbe", false)]
    public async Task Real_host_entrypoints_keep_root_HTTP_JSON_options_and_the_default_host_is_a_negative_control(string name, bool conforms)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-json-host-options-").FullName;
        var receipt = Path.Combine(directory, "receipt.json");
        var hook = Output("Essentials/tests/Architecture/Fixtures/JsonHostOptionsProbe", "JsonHostOptionsProbe");
        var assembly = name == "JsonHostOptionsProbe" ? hook : Output("Essentials/apps/" + name, name);
        await File.WriteAllTextAsync(Path.Combine(directory, "shells.json"), """{"CShells":{"Shells":{"default":{"Name":"default","Features":{}}}}}""");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[] { assembly, "--contentRoot", directory, "--urls", "http://127.0.0.1:0", "--Elsa:Readiness:WarmDefaultShell=false", "--Elsa:Boot:EagerShellActivation:Enabled=false" })
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_STARTUP_HOOKS"] = hook;
        start.Environment["ELSA_JSON_HOST_OPTIONS_RECEIPT"] = receipt;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["Nuplane__Setup__StateFilePath"] = Path.Combine(directory, "nuplane-state.json");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var log = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, log);
            Assert.True(File.Exists(receipt), "No HostBuilt receipt.\n" + log);
            var text = await File.ReadAllTextAsync(receipt);
            using var document = JsonDocument.Parse(text);
            var errors = document.RootElement.GetProperty("Errors").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Equal(name, document.RootElement.GetProperty("Host").GetString());
            var source = typeof(JsonHostEntrypointTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+').Last();
            Assert.EndsWith("+" + source, document.RootElement.GetProperty("Source").GetString(), StringComparison.Ordinal);
            if (conforms) Assert.True(errors.Length == 0, text);
            else
            {
                Assert.Contains("RawJsonElementConverter", errors);
                Assert.Contains("DateTimeOffset admitted an offset-less date", errors);
                Assert.Contains("Raw JSON passthrough", errors);
            }
            if (Environment.GetEnvironmentVariable("ELSA_JSON_HOST_EVIDENCE") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                await File.WriteAllTextAsync(Path.Combine(evidence, name + ".json"), text);
                await File.WriteAllTextAsync(Path.Combine(evidence, name + ".log"), log);
            }
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Output(string directory, string name)
    {
        var configuration = typeof(JsonHostEntrypointTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var path = Path.Combine(RepoPaths.RepoRoot, directory, "bin", configuration, framework, name + ".dll");
        Assert.True(File.Exists(path), "Missing built host/probe: " + path);
        return path;
    }
}
