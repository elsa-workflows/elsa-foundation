using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.Support;

internal sealed class SecretsPackageFeed : IAsyncDisposable
{
    private const string PackageId = "Elsa.Secrets.Persistence.EntityFrameworkCore";
    private const string PackageVersion = "0.0.0-package-feed-proof";
    private readonly string root;

    private SecretsPackageFeed(string root, string moduleAssemblyPath)
    {
        this.root = root;
        ModuleAssemblyPath = moduleAssemblyPath;
    }

    public string ModuleAssemblyPath { get; }

    public static async Task<SecretsPackageFeed> CreateAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetTempPath(), $"elsa-secrets-package-feed-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(root, "packages");
        var extractionDirectory = Path.Combine(root, "extracted");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(extractionDirectory);

        try
        {
            var repositoryRoot = FindRepositoryRoot();
            var project = Path.Combine(
                repositoryRoot,
                "src",
                "Elsa",
                "Secrets",
                "Persistence",
                "EntityFrameworkCore",
                "Elsa.Secrets.Persistence.EntityFrameworkCore.csproj");
            var result = await RunProcessAsync(
                "dotnet",
                [
                    "pack",
                    project,
                    "--configuration",
                    BuildConfiguration(),
                    "--no-restore",
                    "--output",
                    packageDirectory,
                    $"-p:Version={PackageVersion}",
                    "-nodeReuse:false"
                ],
                repositoryRoot,
                cancellationToken,
                new Dictionary<string, string?>
                {
                    ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                    ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
                    ["MSBUILDDISABLENODEREUSE"] = "1"
                });
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Packing the Secrets EF module failed: {result.Describe()}");

            var packagePath = Directory.EnumerateFiles(packageDirectory, $"{PackageId}.*.nupkg")
                .Single(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
            ZipFile.ExtractToDirectory(packagePath, extractionDirectory);
            var nuplaneMetadataPath = Path.Combine(extractionDirectory, "nuplane.json");
            if (!File.Exists(nuplaneMetadataPath))
                throw new FileNotFoundException("The packed Secrets EF module did not contain root Nuplane metadata.", nuplaneMetadataPath);
            var moduleAssemblyPath = Path.Combine(extractionDirectory, "lib", "net10.0", PackageId + ".dll");
            if (!File.Exists(moduleAssemblyPath))
                throw new FileNotFoundException("The packed Secrets EF module DLL was not found.", moduleAssemblyPath);

            return new SecretsPackageFeed(root, Path.GetFullPath(moduleAssemblyPath));
        }
        catch
        {
            DeleteRoot(root);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        DeleteRoot(root);
        return ValueTask.CompletedTask;
    }

    private static async Task<ProbeProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.Equals(fileName, "dotnet", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(dotnetHost))
            fileName = dotnetHost;

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null)
                    startInfo.Environment.Remove(key);
                else
                    startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start '{fileName}'.");

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await Task.WhenAll(outputTask, errorTask).WaitAsync(timeout.Token);
            return new ProbeProcessResult(process.ExitCode, output[0], output[1]);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            KillAndReap(process, outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"The process '{fileName}' exceeded its three-minute timeout.");
        }
    }

    private static string BuildConfiguration() =>
        typeof(SecretsPackageFeed).Assembly
            .GetCustomAttributes(typeof(AssemblyConfigurationAttribute), inherit: false)
            .OfType<AssemblyConfigurationAttribute>()
            .SingleOrDefault()?.Configuration ?? "Release";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the Elsa repository root.");
    }

    private static void KillAndReap(Process process, Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            process.WaitForExitAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(10));
            Task.WhenAll(outputTask, errorTask).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (exception is AggregateException or InvalidOperationException or TimeoutException)
        {
        }
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}

internal sealed record ProbeProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Describe() =>
        $"exit {ExitCode}; stdout={StandardOutput.Trim()}; stderr={StandardError.Trim()}";
}

internal static class SecretsPackageFeedProbeRunner
{
    public const string ProbeAssemblyName = "Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.dll";

    public static async Task<ProbeProcessResult> RunAsync(
        string moduleAssemblyPath,
        string connectionString,
        string migratePolicy,
        CancellationToken cancellationToken = default)
    {
        var probePath = Path.Combine(AppContext.BaseDirectory, "PackageFeedProbe", ProbeAssemblyName);
        if (!File.Exists(probePath))
            throw new FileNotFoundException("The copied Secrets package-feed probe was not found.", probePath);

        return await RunProbeProcessAsync(probePath, moduleAssemblyPath, connectionString, migratePolicy, cancellationToken);
    }

    private static async Task<ProbeProcessResult> RunProbeProcessAsync(
        string probePath,
        string moduleAssemblyPath,
        string connectionString,
        string migratePolicy,
        CancellationToken cancellationToken)
    {
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHost,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(probePath);
        startInfo.Environment["ELSA_SECRETS_PACKAGE_MODULE_PATH"] = moduleAssemblyPath;
        startInfo.Environment["ELSA_SECRETS_PACKAGE_CONNECTION_STRING"] = connectionString;
        startInfo.Environment["ELSA_SECRETS_PACKAGE_MIGRATE_POLICY"] = migratePolicy;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("The Secrets package-feed probe could not be started.");

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await Task.WhenAll(outputTask, errorTask).WaitAsync(timeout.Token);
            return new ProbeProcessResult(process.ExitCode, output[0], output[1]);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("The Secrets package-feed probe exceeded its three-minute timeout.");
        }
    }
}
