namespace Elsa.Studio.Tests;

/// <summary>
/// With <c>Studio:Hosting:Nuplane:EnrollPackageStore</c> off, the shared Studio wiring leaves the package store unenrolled, as a
/// custom host's is by default (#5778): an add-on still installs and is listed, and Nuplane still refuses its browser files.
/// </summary>
public sealed class EssentialsStudioPackageStoreOffTests(EssentialsStudioPackageStoreOffTests.Host host) : IClassFixture<EssentialsStudioPackageStoreOffTests.Host>
{
    [Fact]
    public async Task With_the_switch_off_the_store_stays_unenrolled_and_add_on_files_are_refused()
    {
        var module = await EtlAddOn.InstallAsync(host);

        await EtlAddOn.AssertRefusedAsync(host, module);
        Assert.DoesNotContain("Studio package store", host.OutputSection);
    }

    /// <summary>The Essentials Studio app with the switch turned off over its shipped <c>appsettings.json</c>.</summary>
    public sealed class Host() : StudioAppProcess("Elsa.Studio.Essentials.Host", new Dictionary<string, string>
    {
        ["Studio__Auth__Enabled"] = "false",
        ["Studio__Hosting__Nuplane__EnrollPackageStore"] = "false",
        ["Logging__LogLevel__Elsa.Studio.Hosting.Distribution"] = "Information"
    });
}

/// <summary>
/// The first start enrolls the fresh store; a restart over the same content root reuses that enrollment and serves the add-on
/// installed before it (#5778). The restart kills the process, so it also stands for a crash.
/// </summary>
public sealed class EssentialsStudioPackageStoreRestartTests(EssentialsStudioPackageStoreRestartTests.Host host) : IClassFixture<EssentialsStudioPackageStoreRestartTests.Host>
{
    private const string Enrolled = "Enrolled the fresh Studio package store";
    private const string Reused = "is enrolled (epoch 1)";

    [Fact]
    public async Task A_restart_reuses_the_enrollment_and_still_serves_the_installed_add_on()
    {
        var module = await EtlAddOn.InstallAsync(host);
        await EtlAddOn.AssertServedAsync(host, module);
        var firstProcess = host.ProcessId;

        await host.RestartAsync();

        Assert.NotEqual(firstProcess, host.ProcessId);
        await EtlAddOn.AssertServedAsync(host, await EtlAddOn.ListedAsync(host));
        var output = host.OutputSection;
        Assert.True(Occurrences(output, Enrolled) == 1, $"Expected one first-start enrollment.{output}");
        Assert.True(Occurrences(output, Reused) == 1, $"Expected the restart to reuse the enrollment.{output}");
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    /// <summary>The Essentials Studio app with its shipped configuration, logging its package-store enrollment.</summary>
    public sealed class Host() : StudioAppProcess("Elsa.Studio.Essentials.Host", new Dictionary<string, string>
    {
        ["Studio__Auth__Enabled"] = "false",
        ["Logging__LogLevel__Elsa.Studio.Hosting.Distribution"] = "Information"
    });
}

/// <summary>
/// The switch enrolls only a fresh store. A store that already holds files and is not enrolled may still be in use by another
/// process, so the app refuses to start and says how to proceed, and leaves the files alone (#5778).
/// </summary>
public sealed class EssentialsStudioPackageStoreRefusalTests
{
    [Fact]
    public async Task An_unenrolled_store_that_holds_files_stops_startup_with_a_diagnostic()
    {
        var host = new UnenrolledStoreHost();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(host.InitializeAsync);

            Assert.Contains("exited with code", error.Message);
            Assert.Contains("holds files but is not enrolled", error.Message);
            Assert.Contains("Studio:Hosting:Nuplane:EnrollPackageStore", error.Message);
            Assert.True(File.Exists(Path.Join(host.ContentRoot, UnenrolledStoreHost.InstalledFile)));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>The Essentials Studio app over a content root whose store an earlier, unenrolled run filled.</summary>
    private sealed class UnenrolledStoreHost() : StudioAppProcess("Elsa.Studio.Essentials.Host", new Dictionary<string, string> { ["Studio__Auth__Enabled"] = "false" })
    {
        public static readonly string InstalledFile = Path.Join(".nuplane", "packages", "elsa.studio.etl", "1.0.0", "elsa.studio.etl.nuspec");

        protected override void PrepareContentRoot(string contentRoot)
        {
            var file = Path.Join(contentRoot, InstalledFile);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "<package />");
        }
    }
}
