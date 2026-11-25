using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests;

/// <summary>
/// The built <c>Elsa.Studio.Foundation.Host</c> as a child process, the way an operator runs it in Development: its committed
/// <c>appsettings.json</c>, <c>shells.json</c> and <c>nuplane-management.json</c> in a content root of its own, with an empty
/// <c>packages</c> feed. Nothing of the host is loaded into the test process. One host serves every test here (#5768).
/// </summary>
public sealed class FoundationStudioHostBootTests(FoundationStudioHostBootTests.Host host) : IClassFixture<FoundationStudioHostBootTests.Host>
{
    [Fact]
    public async Task The_foundation_studio_serves_its_index_page()
    {
        var (status, mediaType, body) = await host.GetAsync("/", "text/html");

        Assert.True(status == HttpStatusCode.OK, $"/ answered {status}: {body}{host.OutputSection}");
        Assert.Equal("text/html", mediaType);
        Assert.Contains("<title>Elsa Studio</title>", body);
    }

    /// <summary>
    /// The shell owns Home: <c>/</c> serves the shell, whose bundle registers the Home routes and loads the Home page, empty
    /// state included, from the shell's own assets. The Dashboard module is not compiled in.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_serves_home_from_the_shell_without_the_dashboard_module()
    {
        var (status, _, index) = await host.GetAsync("/", "text/html");
        Assert.True(status == HttpStatusCode.OK, $"/ answered {status}: {index}{host.OutputSection}");
        Assert.Contains("src=\"/studio/assets/index.js\"", index);

        var (shellStatus, _, shell) = await host.GetAsync("/studio/assets/index.js", "text/javascript");
        Assert.True(shellStatus == HttpStatusCode.OK, $"/studio/assets/index.js answered {shellStatus}{host.OutputSection}");
        Assert.Contains("home:", shell);
        Assert.Contains("./HomePage.js", shell);

        var (homeStatus, _, home) = await host.GetAsync("/studio/assets/HomePage.js", "text/javascript");
        Assert.True(homeStatus == HttpStatusCode.OK, $"/studio/assets/HomePage.js answered {homeStatus}{host.OutputSection}");
        Assert.Contains("No installed module adds content to Home yet.", home);

        var (modulesStatus, _, modules) = await host.GetAsync("/_elsa/studio/modules", "application/json");
        Assert.True(modulesStatus == HttpStatusCode.OK, $"/_elsa/studio/modules answered {modulesStatus}: {modules}{host.OutputSection}");
        Assert.DoesNotContain("Elsa.Studio.Dashboard", modules);
    }

    /// <summary>
    /// <c>/modules</c> uploads add-on packages to the feed; <c>/features</c> (FeatureManagement) enables their features. These are
    /// client routes the SPA fallback serves; the API test below proves the administration endpoints behind them are mapped.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_serves_the_module_admin_routes()
    {
        foreach (var route in new[] { "/modules", "/features" })
        {
            var (status, mediaType, body) = await host.GetAsync(route, "text/html");

            Assert.True(status == HttpStatusCode.OK, $"{route} answered {status}: {body}{host.OutputSection}");
            Assert.Equal("text/html", mediaType);
            Assert.Contains("<title>Elsa Studio</title>", body);
        }
    }

    /// <summary>
    /// The add-on administration API is mapped. The SPA fallback answers any HTML path, and <c>/_elsa/{**path}</c> answers
    /// unmapped API paths with 404, so only a mapped, authorization-protected endpoint answers 401 here.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_maps_the_add_on_administration_api()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/module-management/registry", "application/json");

        Assert.True(status == HttpStatusCode.Unauthorized, $"/_elsa/module-management/registry answered {status}: {body}{host.OutputSection}");
    }

    /// <summary>The bare shell lists only module administration and the backend profiles; every other module comes from the feed.</summary>
    [Fact]
    public async Task The_foundation_studio_compiles_in_only_the_bare_shell_modules()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        var modules = JsonNode.Parse(body)!["modules"]!.AsArray().Select(module => module!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(["Elsa.Studio.Backends", "Elsa.Studio.FeatureManagement"], modules);
    }

    /// <summary>Every feature <c>shells.json</c> enables is compiled in, so the shell activates without a warning.</summary>
    [Fact]
    public async Task The_foundation_studio_activates_its_shell_without_warnings()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        Assert.DoesNotContain("warn:", host.OutputSection);
        Assert.DoesNotContain("fail:", host.OutputSection);
    }

    /// <summary>The Foundation Studio app, started once for the class from its committed Development configuration.</summary>
    public sealed class Host : IAsyncLifetime
    {
        private const string Name = "Elsa.Studio.Foundation.Host";
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

        private readonly StringBuilder _output = new();
        private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HttpClient _client = new();
        private Process? _process;
        private string? _contentRoot;

        /// <summary>The host's console output so far, for assertion messages.</summary>
        public string OutputSection
        {
            get
            {
                lock (_output)
                    return $"{Environment.NewLine}Host output:{Environment.NewLine}{_output}";
            }
        }

        public async Task InitializeAsync()
        {
            var source = Path.Join(StudioRoot, "src", "apps", Name);
            var assembly = Path.Join(source, "bin", Configuration, Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)), Name + ".dll");
            if (!File.Exists(assembly))
                throw new FileNotFoundException($"Build {Name} ({Configuration}) before running these tests.", assembly);

            _contentRoot = Directory.CreateTempSubdirectory("elsa-studio-foundation-boot-").FullName;
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json", "shells.json", "nuplane-management.json" })
                File.Copy(Path.Join(source, file), Path.Join(_contentRoot, file));
            Directory.CreateDirectory(Path.Join(_contentRoot, "packages"));

            // Kestrel reserves its own ephemeral port: picking a free one and releasing it races other test processes.
            var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                [assembly, "--contentRoot", _contentRoot, "--urls", "http://127.0.0.1:0"])
            {
                WorkingDirectory = _contentRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (_, line) => Append(line.Data);
            _process.ErrorDataReceived += (_, line) => Append(line.Data);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
            while (true)
            {
                if (_process.HasExited)
                    throw new InvalidOperationException($"{Name} exited with code {_process.ExitCode} before it listened.{OutputSection}");
                if (_listening.Task.IsCompletedSuccessfully)
                {
                    _client.BaseAddress = await _listening.Task;
                    return;
                }
                if (DateTimeOffset.UtcNow > deadline)
                    throw new InvalidOperationException($"{Name} did not listen within {ReadyTimeout}.{OutputSection}");

                await Task.Delay(250);
            }
        }

        /// <summary>The status, media type and body a <c>GET</c> of <paramref name="path"/> accepting <paramref name="accept"/> is answered with.</summary>
        public async Task<(HttpStatusCode Status, string? MediaType, string Body)> GetAsync(string path, string accept)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            using var response = await _client.SendAsync(request);
            return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(StopTimeout);
                    await _process.WaitForExitAsync(timeout.Token);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or OperationCanceledException)
            {
                // Exited between the check and the kill, or would not die in time: nothing left here to act on.
            }
            finally
            {
                _process?.Dispose();
                if (_contentRoot is not null)
                    TryDelete(_contentRoot);
            }
        }

        private void Append(string? line)
        {
            if (line is null)
                return;
            lock (_output)
                _output.AppendLine(line);

            const string marker = "Now listening on: ";
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && Uri.TryCreate(line[(index + marker.Length)..].Trim(), UriKind.Absolute, out var address) && address.Port > 0)
                _listening.TrySetResult(address);
        }

        /// <summary>A temp directory left behind is harmless; failing the test over it, or masking an earlier failure, is not.</summary>
        private static void TryDelete(string directory)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        /// <summary>The build configuration this assembly was built with, which the host is built in too.</summary>
        private static string Configuration { get; } = typeof(Host).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

        private static string StudioRoot { get; } = FindStudioRoot();

        private static string FindStudioRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Studio.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException($"No Elsa.Studio.slnx above {AppContext.BaseDirectory}.");
        }
    }
}
