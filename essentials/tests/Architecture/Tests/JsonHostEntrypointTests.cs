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
        Process? process = null;
        Task<string>? stdout = null, stderr = null;
        Exception? failure = null;
        try
        {
            var hook = Output("essentials/tests/Architecture/Fixtures/JsonHostOptionsProbe", "JsonHostOptionsProbe");
            var assembly = name == "JsonHostOptionsProbe" ? hook : Output("apps/" + name, name);
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
            process = Process.Start(start)!;
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var log = await stdout.WaitAsync(TimeSpan.FromSeconds(10)) + await stderr.WaitAsync(TimeSpan.FromSeconds(10));
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
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            var cleanupErrors = new List<Exception>();
            await CleanupAsync(async () =>
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                }
            });
            string capturedOut = string.Empty, capturedError = string.Empty;
            await CleanupAsync(async () => { if (stdout is not null) capturedOut = await stdout.WaitAsync(TimeSpan.FromSeconds(10)); });
            await CleanupAsync(async () => { if (stderr is not null) capturedError = await stderr.WaitAsync(TimeSpan.FromSeconds(10)); });
            if (Environment.GetEnvironmentVariable("ELSA_JSON_HOST_EVIDENCE") is { Length: > 0 } evidence)
            {
                string? retained = null;
                await CleanupAsync(() =>
                {
                    retained = Directory.CreateDirectory(Path.Combine(evidence, name + "-" + Guid.NewGuid().ToString("N"))).FullName;
                    return Task.CompletedTask;
                });
                if (retained is not null)
                {
                    await CleanupAsync(() => File.WriteAllTextAsync(Path.Combine(retained, "host.log"), capturedOut + capturedError));
                    await CleanupAsync(async () =>
                    {
                        if (File.Exists(receipt)) File.Copy(receipt, Path.Combine(retained, "receipt.json"));
                        else await File.WriteAllTextAsync(Path.Combine(retained, "missing-receipt.txt"), "HostBuilt did not produce a receipt.");
                    });
                    if (failure is not null) await CleanupAsync(() => File.WriteAllTextAsync(Path.Combine(retained, "failure.txt"), failure.ToString()));
                }
            }
            await CleanupAsync(() => { process?.Dispose(); return Task.CompletedTask; });
            await CleanupAsync(() => { Directory.Delete(directory, recursive: true); return Task.CompletedTask; });
            if (cleanupErrors.Count > 0)
            {
                var error = new AggregateException("Host probe cleanup/evidence failed.", cleanupErrors);
                if (failure is null) throw error;
                Console.Error.WriteLine("In addition to the original host probe failure: " + error);
            }

            async Task CleanupAsync(Func<Task> action)
            {
                try { await action(); }
                catch (Exception error) { cleanupErrors.Add(error); }
            }
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
