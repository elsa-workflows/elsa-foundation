using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Owns one local issuer, signing key, token factory, isolated IAM/Runtime files and every child process in an actor
/// scenario. Child startup and fixture controls travel over redirected stdin/stdout; credentials and database paths
/// never appear in process arguments or retained receipts.
/// </summary>
internal sealed class WorkerOidcHostFixture : IAsyncDisposable
{
    public const string RequestOperationIdHeader = "X-Worker-Oidc-Fixture-Operation";
    public const string TenantId = "worker-tenant-acme";
    public const string ProviderId = "worker-issuer";
    public const string Audience = "worker-api";
    public const string NormalizedAuthenticationType = "Elsa.Foundation.Identity.Oidc.Bearer.Normalized";
    public const string JwtBearerScheme = "Elsa.Identity.Oidc.Jwt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly List<WorkerOidcHostProcess> _hosts = [];
    private readonly WorkerOidcIssuer _issuer;
    private bool _disposed;

    private WorkerOidcHostFixture(string root, WorkerOidcIssuer issuer)
    {
        _root = root;
        _issuer = issuer;
    }

    public string Authority => _issuer.Authority;

    public string RuntimeDatabasePath => Path.Combine(_root, "runtime.db");

    public string IamDatabasePath => Path.Combine(_root, "iam.db");

    public WorkerProfileCandidate PrimaryCandidate { get; private set; } = null!;

    public WorkerProfileCandidate? ControlCandidate { get; private set; }

    public static async Task<WorkerOidcHostFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"elsa-worker-oidc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "locks"));
        WorkerOidcIssuer? issuer = null;
        WorkerOidcHostFixture? fixture = null;
        try
        {
            issuer = await WorkerOidcIssuer.StartAsync();
            fixture = new WorkerOidcHostFixture(root, issuer);
            fixture.PrimaryCandidate = await WorkerProfileCandidate.CreatePrimaryAsync(
                root, issuer.Authority, fixture.RuntimeDatabasePath, fixture.IamDatabasePath, Path.Combine(root, "locks"));
            return fixture;
        }
        catch
        {
            if (fixture is not null)
                await fixture.DisposeAsync();
            else
            {
                if (issuer is not null)
                    await issuer.DisposeAsync();
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            throw;
        }
    }

    public string CreateToken(
        string subject = "worker-actor",
        string? issuer = null,
        string? audience = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? expires = null,
        params Claim[] claims) =>
        _issuer.CreateToken(subject, issuer, audience, notBefore, expires, claims);

    public string TamperSignature(string token) => _issuer.TamperSignature(token);

    public async Task<WorkerProfileCandidate> CreateControlCandidateAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ControlCandidate is not null)
            return ControlCandidate;
        ControlCandidate = await PrimaryCandidate.CreateControlAsync(
            _root, Authority, RuntimeDatabasePath, IamDatabasePath, Path.Combine(_root, "locks"));
        return ControlCandidate;
    }

    public async Task<WorkerOidcHostProcess> StartHostAsync(
        WorkerProfileCandidate? candidate = null,
        string? persistenceScope = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        candidate ??= PrimaryCandidate;
        var startup = new WorkerOidcHostStartup(
            candidate.CandidateDirectory,
            candidate.ShellId,
            candidate.Environment,
            persistenceScope ?? TenantId);
        var assemblyPath = WorkerHostAssemblyPath();
        var artifactSha256 = await HashFileSha256Async(assemblyPath);
        var process = await WorkerOidcHostProcess.StartAsync(
            assemblyPath,
            artifactSha256,
            _root,
            startup,
            JsonOptions);
        _hosts.Add(process);
        return process;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<Exception>? failures = null;
        foreach (var host in _hosts)
        {
            try
            {
                await host.DisposeAsync();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        try
        {
            await _issuer.DisposeAsync();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            foreach (var database in new[] { IamDatabasePath, RuntimeDatabasePath })
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = database + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }

            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is { Count: > 0 })
            throw new AggregateException("Worker OIDC fixture cleanup failed.", failures);
    }

    private static string WorkerHostAssemblyPath()
    {
        var configuration = typeof(WorkerOidcHostFixture).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var targetFramework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var repository = FindRepositoryRoot();
        var path = Path.Combine(
            repository,
            "tests",
            "essentials",
            "Workflows",
            "Runtime",
            "Persistence",
            "EntityFrameworkCore",
            "Fixtures",
            "WorkerOidcHost",
            "bin",
            configuration,
            targetFramework,
            "WorkerOidcHost.dll");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("Build the Worker OIDC fixture executable before running its actor proof.", path);
    }

    private static async Task<string> HashFileSha256Async(string path)
    {
        await using var assembly = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(assembly));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("The Worker OIDC fixture could not locate the repository root.");
    }
}

internal sealed record WorkerOidcHostStartup(
    string CandidateDirectory,
    string ShellId,
    string Environment,
    string PersistenceScope);

internal sealed class WorkerOidcHostProcess : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);
    private readonly Process _process;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private readonly Task _stderrDrain;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    private WorkerOidcHostProcess(Process process, WorkerOidcHostReply ready, JsonSerializerOptions jsonOptions, Task stderrDrain)
    {
        _process = process;
        Ready = ready;
        _jsonOptions = jsonOptions;
        _client = new HttpClient { BaseAddress = new Uri(ready.Address!, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(30) };
        _stderrDrain = stderrDrain;
    }

    public WorkerOidcHostReply Ready { get; }

    public HttpClient Client => _client;

    public int ProcessId => Ready.ProcessId;

    public long ProcessStartUtcTicks => Ready.ProcessStartUtcTicks;

    public string ArtifactSha256 => Ready.ArtifactSha256 ?? throw new InvalidOperationException("The Worker OIDC child omitted its artifact hash.");

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public static async Task<WorkerOidcHostProcess> StartAsync(
        string assemblyPath,
        string expectedArtifactSha256,
        string workingDirectory,
        WorkerOidcHostStartup startup,
        JsonSerializerOptions jsonOptions)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The Worker OIDC fixture process did not start.");
        }

        var stderrDrain = DrainAsync(process.StandardError);
        WorkerOidcHostReply? ready = null;
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(startup, jsonOptions));
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(StartupTimeout);
            ready = line is null ? null : JsonSerializer.Deserialize<WorkerOidcHostReply>(line, jsonOptions);
            if (ready?.Status != "ready" || ready.Address is null)
                throw new InvalidOperationException(
                    $"The Worker OIDC fixture child did not become ready ({ready?.Code ?? "no-receipt"}, " +
                    $"{ready?.Stage ?? "unknown-stage"}, {ready?.ExceptionType ?? "unknown-exception-type"}).");
            if (!string.Equals(ready.ArtifactSha256, expectedArtifactSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The Worker OIDC child artifact hash did not match the selected executable.");

            var host = new WorkerOidcHostProcess(process, ready, jsonOptions, stderrDrain);
            return host;
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await stderrDrain;
            process.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> ControlAsync(string command, object? payload = null)
    {
        var receipt = await ControlWithReceiptAsync(command, payload);
        return receipt.Data;
    }

    public async Task<WorkerOidcControlReceipt> ControlWithReceiptAsync(
        string command,
        object? payload = null,
        bool resetEpochAfterOperationEntry = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _controlGate.WaitAsync();
        try
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
            {
                command,
                payload = payload ?? new { },
                resetEpochAfterOperationEntry
            }, _jsonOptions));
            await _process.StandardInput.FlushAsync();
            var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(ControlTimeout);
            var reply = line is null ? null : JsonSerializer.Deserialize<WorkerOidcHostReply>(line, _jsonOptions);
            if (reply?.Status != "ok")
                throw new InvalidOperationException($"The Worker OIDC fixture control failed ({reply?.Code ?? "no-receipt"}).");
            return new WorkerOidcControlReceipt(
                reply.OperationId ?? throw new InvalidOperationException("The Worker OIDC fixture control omitted its operation ID."),
                reply.OperationKind ?? throw new InvalidOperationException("The Worker OIDC fixture control omitted its operation kind."),
                reply.OperationCategory ?? throw new InvalidOperationException("The Worker OIDC fixture control omitted its operation category."),
                reply.OperationEpoch ?? throw new InvalidOperationException("The Worker OIDC fixture control omitted its operation epoch."),
                reply.Data.Clone());
        }
        finally
        {
            _controlGate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_process.HasExited)
        {
            await _process.WaitForExitAsync();
            return;
        }

        await ControlAsync("shutdown");
        await _process.WaitForExitAsync().WaitAsync(ControlTimeout);
        await _stderrDrain;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _client.Dispose();

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    // StopAsync uses the private control channel and waits for a clean, observable child exit.
                    _disposed = false;
                    await StopAsync();
                }
                catch
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync();
                    }
                }
            }
            await _stderrDrain;
        }
        finally
        {
            _disposed = true;
            _process.Dispose();
            _controlGate.Dispose();
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null)
        {
            // The child must remain bounded and value-free; stderr is drained and discarded, never retained.
        }
    }
}

internal sealed record WorkerOidcHostReply(
    string Status,
    string? Code,
    string? Address,
    int ProcessId,
    long ProcessStartUtcTicks,
    JsonElement Data,
    string? ArtifactSha256,
    string? Stage,
    string? ExceptionType,
    string? OperationId = null,
    string? OperationKind = null,
    string? OperationCategory = null,
    long? OperationEpoch = null);

internal sealed record WorkerOidcControlReceipt(
    string OperationId,
    string OperationKind,
    string OperationCategory,
    long OperationEpoch,
    JsonElement Data);

internal sealed class WorkerOidcIssuer : IAsyncDisposable
{
    private const string KeyId = "worker-oidc-fixture-key";
    private readonly RSA _signingKey;
    private readonly WebApplication _app;

    private WorkerOidcIssuer(RSA signingKey, WebApplication app, string authority)
    {
        _signingKey = signingKey;
        _app = app;
        Authority = authority;
    }

    public string Authority { get; }

    public static async Task<WorkerOidcIssuer> StartAsync()
    {
        var key = RSA.Create(2048);
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            app = builder.Build();
            app.MapGet("/.well-known/openid-configuration", (HttpContext context) =>
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                var issuer = IssuerOrigin(context);
                return Results.Json(new
                {
                    issuer,
                    jwks_uri = $"{issuer}/.well-known/jwks.json",
                    subject_types_supported = new[] { "public" },
                    id_token_signing_alg_values_supported = new[] { "RS256" },
                    response_types_supported = new[] { "id_token" }
                });
            });
            app.MapGet("/.well-known/jwks.json", (HttpContext context) =>
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                var parameters = key.ExportParameters(false);
                return Results.Json(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = KeyId,
                            alg = "RS256",
                            n = Base64Url(parameters.Modulus!),
                            e = Base64Url(parameters.Exponent!)
                        }
                    }
                });
            });
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            var authority = addresses?.Single(address => address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                .TrimEnd('/') ?? throw new InvalidOperationException("The local issuer did not bind a loopback address.");
            return new WorkerOidcIssuer(key, app, authority);
        }
        catch
        {
            if (app is not null)
                await app.DisposeAsync();
            key.Dispose();
            throw;
        }
    }

    public string CreateToken(
        string subject,
        string? issuer,
        string? audience,
        DateTimeOffset? notBefore,
        DateTimeOffset? expires,
        IEnumerable<Claim> claims)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["iss"] = issuer ?? Authority,
            ["sub"] = subject,
            ["aud"] = audience ?? WorkerOidcHostFixture.Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = (notBefore ?? now.AddMinutes(-1)).ToUnixTimeSeconds(),
            ["exp"] = (expires ?? now.AddHours(2)).ToUnixTimeSeconds()
        };
        foreach (var group in claims.GroupBy(claim => claim.Type, StringComparer.Ordinal))
        {
            var values = group.Select(claim => claim.Value).ToArray();
            payload[group.Key] = values.Length == 1 ? values[0] : values;
        }

        var header = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = KeyId
        };
        var signingInput = $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var signature = _signingKey.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    public string TamperSignature(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[2].Length == 0)
            throw new ArgumentException("The supplied token is not a compact JWT.", nameof(token));
        var replacement = parts[2][0] == 'A' ? 'B' : 'A';
        parts[2] = replacement + parts[2][1..];
        return string.Join('.', parts);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _signingKey.Dispose();
    }

    private static string IssuerOrigin(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host}";

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
