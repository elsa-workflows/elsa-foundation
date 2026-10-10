using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Xml.Linq;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Nuplane;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane;
using Nuplane.Abstractions;
using Nuplane.Feeds.Configuration;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.Models;
using Nuplane.Sources.Directory.Builder;
using Nuplane.Store.State;
using static ConsumerChecks;

const string fixtureId = "ConsumerFixture.ShellFeature";
const string fixtureVersion = "1.0.0-preview.1";
const string fixtureFeatureId = "consumer-fixture-shell-feature";
const string sharedPackageId = "CShells.Abstractions";
const string shellName = "consumer-shell";
string[] cshellsFamilyPackageIds =
[
    "CShells",
    "CShells.Abstractions",
    "CShells.AspNetCore",
    "CShells.AspNetCore.Abstractions",
    "CShells.AspNetCore.Testing",
    "CShells.FastEndpoints",
    "CShells.FastEndpoints.Abstractions",
    "CShells.Management.Api",
    "CShells.Nuplane",
    "CShells.Providers.FluentStorage"
];

if (args.Length != 6 || !int.TryParse(args[0], out var expectedRuntimeMajor))
    throw new ArgumentException("Pass runtime major, exact CShells and Nuplane package versions, both verified archive directories, and the fixture archive directory.");
var expectedAdapterVersion = args[1];
var nuplanePackageVersion = args[2];
var cshellsArchiveDirectory = Path.GetFullPath(args[3]);
var nuplaneArchiveDirectory = Path.GetFullPath(args[4]);
var fixtureArchiveDirectory = Path.GetFullPath(args[5]);
Require(Environment.Version.Major == expectedRuntimeMajor,
    $"Expected .NET {expectedRuntimeMajor}, running {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");

var artifactRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var consumerInputs = new ConsumerInputs(artifactRoot, fixtureArchiveDirectory);
var fixtureArchive = ArchiveFiles.Find(fixtureArchiveDirectory, $"{fixtureId}.{fixtureVersion}.nupkg");
var fixtureSharedArchive = ArchiveFiles.Find(fixtureArchiveDirectory, $"{sharedPackageId}.0.0.30-preview.165.nupkg");
var sharedArchive = ArchiveFiles.Find(cshellsArchiveDirectory, $"{sharedPackageId}.{expectedAdapterVersion}.nupkg");
var nuplaneArchive = ArchiveFiles.Find(nuplaneArchiveDirectory, $"Nuplane.{nuplanePackageVersion}.nupkg");
var nuplaneLoadingArchive = ArchiveFiles.Find(nuplaneArchiveDirectory, $"Nuplane.Loading.{nuplanePackageVersion}.nupkg");
var nuplaneLoadingAbstractionsArchive = ArchiveFiles.Find(nuplaneArchiveDirectory, $"Nuplane.Loading.Abstractions.{nuplanePackageVersion}.nupkg");
var nuplaneDirectoryArchive = ArchiveFiles.Find(nuplaneArchiveDirectory, $"Nuplane.Sources.Directory.{nuplanePackageVersion}.nupkg");
var cshellsArchive = ArchiveFiles.Find(cshellsArchiveDirectory, $"CShells.{expectedAdapterVersion}.nupkg");
var adapterArchive = ArchiveFiles.Find(cshellsArchiveDirectory, $"CShells.Nuplane.{expectedAdapterVersion}.nupkg");
var cshellsFamilyArchives = cshellsFamilyPackageIds
    .Select(packageId => ArchiveFiles.Find(cshellsArchiveDirectory, $"{packageId}.{expectedAdapterVersion}.nupkg"))
    .ToArray();
Require(File.Exists(fixtureArchive) && File.Exists(fixtureSharedArchive) && File.Exists(sharedArchive), "The producer fixture, its runtime-feed contract archive, and the under-test host contract archive are required.");
Require(new[] { nuplaneArchive, nuplaneLoadingArchive, nuplaneLoadingAbstractionsArchive, nuplaneDirectoryArchive, cshellsArchive }.All(File.Exists), "A required verified Nuplane or CShells package archive is missing.");
Require(cshellsFamilyArchives.All(File.Exists), "The verified CShells archive directory must contain all ten family packages at the requested version.");
Require(File.Exists(adapterArchive), $"The verified CShells archive directory is missing '{Path.GetFileName(adapterArchive)}'.");
Require(ReadNuspecVersion(nuplaneArchive, "Nuplane") == nuplanePackageVersion, "Unexpected Nuplane runtime package version.");
Require(ReadNuspecVersion(fixtureSharedArchive, sharedPackageId) == "0.0.30-preview.165", "Unexpected fixture runtime-feed shared-contract package version.");
Require(ReadNuspecVersion(sharedArchive, sharedPackageId) == expectedAdapterVersion, "Unexpected under-test shared-contract package version.");
Require(ReadNuspecVersion(cshellsArchive, "CShells") == expectedAdapterVersion, "Unexpected under-test CShells runtime package version.");
for (var index = 0; index < cshellsFamilyArchives.Length; index++)
    Require(ReadNuspecVersion(cshellsFamilyArchives[index], cshellsFamilyPackageIds[index]) == expectedAdapterVersion,
        $"Unexpected package identity or version in '{Path.GetFileName(cshellsFamilyArchives[index])}'.");
Require(ReadNuspecVersion(fixtureArchive, fixtureId) == fixtureVersion, "Unexpected fixture package version.");
Require(ReadNuspecVersion(adapterArchive, "CShells.Nuplane") == expectedAdapterVersion, "The adapter package archive version does not match the requested version.");

var targetFramework = $"net{Environment.Version.Major}.0";
var adapterAssemblyEvidence = VerifyPackageAssembly(typeof(CShellsNuplaneBuilderExtensions).Assembly, adapterArchive, targetFramework);
var nuplaneAssemblyEvidence = VerifyPackageAssembly(typeof(NuplaneServiceCollectionExtensions).Assembly, nuplaneArchive, targetFramework);
var nuplaneLoadingAssemblyEvidence = VerifyPackageAssembly(typeof(NuplaneBuilderLoadingExtensions).Assembly, nuplaneLoadingArchive, targetFramework);
var nuplaneLoadingAbstractionsEvidence = VerifyPackageAssembly(typeof(IPackageAssemblyCatalog).Assembly, nuplaneLoadingAbstractionsArchive, targetFramework);
var nuplaneDirectoryAssemblyEvidence = VerifyPackageAssembly(typeof(NuplaneBuilderDirectoryExtensions).Assembly, nuplaneDirectoryArchive, targetFramework);
var cshellsAssemblyEvidence = VerifyPackageAssembly(typeof(CShellsBuilder).Assembly, cshellsArchive, targetFramework);
var sharedAssemblyEvidence = VerifyPackageAssembly(typeof(IShellFeature).Assembly, sharedArchive, targetFramework);

await VerifyLateReloadDrainCaptureCleanupAsync(consumerInputs);

// Compile and compose the exact one-callback overload shown by the package quickstart.
using (var quickstartProvider = ComposeDocumentedQuickstart())
    Require(quickstartProvider.GetRequiredService<IOptions<NuplaneIntegrationOptions>>().Value.Enabled,
        "Documented quickstart failed to register the generic options.");

await RunScenarioAsync(consumerInputs, "cold-deferred", fixtureInitiallyPresent: true, failFirstReloadCallback: false, configure: null, async scenario =>
{
    var options = scenario.Provider.GetRequiredService<IOptions<NuplaneIntegrationOptions>>().Value;
    Require(options.Enabled, "The generic adapter default must enable observer integration.");
    Require(options.RefreshTrigger == NuplaneRefreshTrigger.ChangedOrPending, "The generic refresh default must be ChangedOrPending.");
    Require(!options.AutoReload, "The generic auto-reload default must remain disabled.");

    var catalog = scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>();
    RequireThrows<InvalidOperationException>(() => _ = catalog.CurrentSnapshot,
        "The runtime feature catalog must remain uninitialized before a requested build.");
    var first = await scenario.ReconcileAsync("cold-package-add");
    Require(first.ChangeSet.Added.Any(package => package.Id.Equals(fixtureId, StringComparison.OrdinalIgnoreCase)),
        "The real directory feed did not add the fixture package.");
    RequireThrows<InvalidOperationException>(() => _ = catalog.CurrentSnapshot,
        "A package completion with no active shell must defer catalog refresh until a real build.");

    var shell = await scenario.AwaitOperationAsync(
        scenario.Provider.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName, scenario.Token), "cold shell activation");
    var markerType = await scenario.RequireFixtureBuiltAsync(shell);
    Require((await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "cold runtime catalog snapshot")).FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId),
        "The first real shell build did not use the deferred package feature snapshot.");
    scenario.WriteResult("coldDeferredRefresh", new
    {
        options.Enabled,
        options.RefreshTrigger,
        options.AutoReload,
        BuildGeneration = shell.Descriptor.Generation,
        MarkerType = markerType.FullName,
        DeferredRefreshOccurredAtBuild = true
    });
});

await RunScenarioAsync(consumerInputs, "cold-changed-or-pending-auto-reload", fixtureInitiallyPresent: true, failFirstReloadCallback: false, configure: options =>
{
    options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
    options.AutoReload = true;
}, async scenario =>
{
    var registry = scenario.Provider.GetRequiredService<IShellRegistry>();
    var catalog = scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>();
    var add = await scenario.ReconcileAsync("changed-or-pending-cold-add");
    Require(add.ChangeSet.Added.Any(package => package.Id.Equals(fixtureId, StringComparison.OrdinalIgnoreCase)),
        "The cold ChangedOrPending scenario did not add the fixture before any shell existed.");
    RequireThrows<InvalidOperationException>(() => _ = catalog.CurrentSnapshot,
        "The cold ChangedOrPending adapter should defer refresh until a build needs the catalog.");

    var first = await scenario.AwaitOperationAsync(
        registry.GetOrActivateAsync(shellName, scenario.Token), "cold shell activation");
    var markerType = await scenario.RequireFixtureBuiltAsync(first);
    var firstSnapshot = await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "initial catalog snapshot");
    Require(firstSnapshot.FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId),
        "The first cold shell build did not include the package feature.");

    var unchanged = await scenario.ReconcileAsync("changed-or-pending-cold-unchanged");
    Require(unchanged.ChangeSet.Added.Count + unchanged.ChangeSet.Updated.Count + unchanged.ChangeSet.Removed.Count == 0,
        "The cold ChangedOrPending follow-up must be a real unchanged reconciliation.");
    Require(scenario.Journal.TrailingObserverCount == 2 && scenario.Journal.TrailingObservations[1].AppliedCount > 0,
        "Nuplane did not deliver the unchanged eligible completion with active applied packages.");
    var current = registry.GetActive(shellName) ?? throw new InvalidOperationException("The unchanged completion left no active shell.");
    var currentSnapshot = await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "unchanged catalog snapshot");
    Require(currentSnapshot.Generation == firstSnapshot.Generation,
        "ChangedOrPending refreshed the catalog for an unchanged completion with no pending refresh.");
    Require(current.Descriptor.Generation == first.Descriptor.Generation,
        "ChangedOrPending auto-reloaded on an unchanged completion with no pending reload.");
    Require(scenario.Journal.ReloadCallbackAttempts == 0,
        "ChangedOrPending reported an automatic reload result when it did not reload.");
    Require(current.ServiceProvider.GetService(markerType) is not null,
        "The unchanged shell generation lost the installed fixture feature.");

    scenario.WriteResult("coldChangedOrPendingAutoReload", new
    {
        catalogGenerationAfterFirstBuild = firstSnapshot.Generation,
        shellGenerationAfterFirstBuild = first.Descriptor.Generation,
        catalogGenerationAfterUnchanged = currentSnapshot.Generation,
        shellGenerationAfterUnchanged = current.Descriptor.Generation,
        unchangedCompletionAppliedPackages = scenario.Journal.TrailingObservations[1].AppliedCount,
        unchangedCompletionDidNotRefreshOrReload = true
    });
});

await RunScenarioAsync(consumerInputs, "host-auto-reload-profile", fixtureInitiallyPresent: true, failFirstReloadCallback: false, configure: options =>
{
    options.RefreshTrigger = NuplaneRefreshTrigger.EveryEligibleCompletion;
    options.AutoReload = true;
}, async scenario =>
{
    var registry = scenario.Provider.GetRequiredService<IShellRegistry>();
    var initial = await scenario.ReconcileAsync("host-deferred-package-add");
    Require(initial.ChangeSet.Added.Any(package => package.Id.Equals(fixtureId, StringComparison.OrdinalIgnoreCase)),
        "The initial host package was not loaded before the first shell build.");
    var original = await scenario.AwaitOperationAsync(registry.GetOrActivateAsync(shellName, scenario.Token), "host shell activation");
    var markerType = await scenario.RequireFixtureBuiltAsync(original);
    var originalGeneration = original.Descriptor.Generation;

    var unchanged = await scenario.ReconcileAsync("host-every-eligible-unchanged");
    Require(unchanged.ChangeSet.Added.Count + unchanged.ChangeSet.Updated.Count + unchanged.ChangeSet.Removed.Count == 0,
        "The host-profile refresh/reload cycle was intended to be unchanged.");
    var afterUnchanged = registry.GetActive(shellName) ?? throw new InvalidOperationException("Automatic reload left no active shell.");
    Require(afterUnchanged.Descriptor.Generation > originalGeneration, "EveryEligibleCompletion did not reload the active shell on an unchanged eligible completion.");
    _ = await scenario.RequireFixtureBuiltAsync(afterUnchanged);
    Require(scenario.Journal.ReloadCallbackAttempts == 1 && !scenario.Journal.LastReloadCallbackFailed,
        "The host reload-results callback did not receive the successful unchanged-cycle reload.");
    Require(scenario.Journal.TrailingObserverCount == 2 && scenario.Journal.TrailingObservations[1].AppliedCount > 0,
        "Nuplane did not deliver an actual unchanged completion with applied packages to the observer after the adapter.");

    scenario.RemoveFixtureFromFeed();
    var finalRemoval = await scenario.ReconcileAsync("host-final-removal-to-zero");
    Require(finalRemoval.ChangeSet.Removed.Contains(fixtureId, StringComparer.OrdinalIgnoreCase), "The final host-profile removal was not applied.");
    var emptyShell = registry.GetActive(shellName) ?? throw new InvalidOperationException("The final removal left no serving shell.");
    Require(emptyShell.Descriptor.Generation > afterUnchanged.Descriptor.Generation, "The host profile failed to reload after the last removal.");
    Require(emptyShell.ServiceProvider.GetService(markerType) is null, "The final host-profile generation still contains the removed package feature service.");
    Require(!(await scenario.AwaitOperationAsync(scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(scenario.Token), "host final catalog snapshot"))
        .FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId), "The final host-profile feature catalog is not empty.");
    Require(scenario.Journal.ReloadCallbackAttempts == 2 && scenario.Journal.TrailingObserverCount == 3,
        "Expected one adapter callback and one trailing observer delivery per eligible completion with applied packages.");

    var catalog = scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>();
    var emptyCatalogGeneration = (await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "catalog before quiet cycle")).Generation;
    var quiet = await scenario.ReconcileAsync("host-empty-quiet-cycle-after-final-removal");
    Require(quiet.ChangeSet.Added.Count + quiet.ChangeSet.Updated.Count + quiet.ChangeSet.Removed.Count == 0,
        "The final empty quiet cycle must have no package changes.");
    Require(scenario.Journal.TrailingObserverCount == 3 && scenario.Journal.ReloadCallbackAttempts == 2,
        "An empty quiet cycle fabricated a completion or automatic reload callback.");
    Require(registry.GetActive(shellName)?.Descriptor.Generation == emptyShell.Descriptor.Generation,
        "An empty quiet cycle unexpectedly replaced the serving generation.");
    Require((await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "catalog after quiet cycle")).Generation == emptyCatalogGeneration,
        "An empty quiet cycle unexpectedly refreshed the feature catalog.");

    scenario.WriteResult("foundationHostProfile", new
    {
        RefreshTrigger = NuplaneRefreshTrigger.EveryEligibleCompletion,
        AutoReload = true,
        originalGeneration,
        generationAfterUnchangedCompletion = afterUnchanged.Descriptor.Generation,
        finalGeneration = emptyShell.Descriptor.Generation,
        unchangedCompletionReloaded = true,
        appliedPackagesDeliveredForUnchangedCompletion = scenario.Journal.TrailingObservations[1].AppliedCount,
        finalCatalogAndGenerationOmitFixture = true,
        emptyQuietCycleDidNotDeliverRefreshOrReload = true,
        reloadCallbackObservations = scenario.Journal.ReloadObservations,
        trailingObserverObservations = scenario.Journal.TrailingObservations
    });
});

await RunScenarioAsync(consumerInputs, "changed-or-pending-reload-retry", fixtureInitiallyPresent: false, failFirstReloadCallback: true, configure: options =>
{
    options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
    options.AutoReload = true;
}, async scenario =>
{
    var registry = scenario.Provider.GetRequiredService<IShellRegistry>();
    var initiallyEmpty = await scenario.AwaitOperationAsync(registry.GetOrActivateAsync(shellName, scenario.Token), "retry initial empty shell activation");
    var firstGeneration = initiallyEmpty.Descriptor.Generation;
    Require(!(await scenario.AwaitOperationAsync(scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(scenario.Token), "retry initial catalog snapshot"))
        .FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId), "The retry scenario must start with an empty catalog.");

    scenario.AddFixtureToFeed();
    var added = await scenario.ReconcileAsync("changed-or-pending-first-add");
    Require(added.ChangeSet.Added.Any(package => package.Id.Equals(fixtureId, StringComparison.OrdinalIgnoreCase)), "The retry scenario did not apply its first package add.");
    var afterFailedCallback = registry.GetActive(shellName) ?? throw new InvalidOperationException("The first auto reload left no shell active.");
    Require(afterFailedCallback.Descriptor.Generation > firstGeneration, "The changed event did not auto-reload the active shell.");
    var markerType = await scenario.RequireFixtureBuiltAsync(afterFailedCallback);
    Require(scenario.Journal.ReloadCallbackAttempts == 1 && scenario.Journal.FirstReloadCallbackFailed
            && scenario.Journal.ReloadObservations.Single().EveryResultSucceeded
            && scenario.Journal.ReloadObservations.Single().DeliberateFailure,
        "The first callback should fail deliberately after one successful shell reload result.");
    Require(scenario.Journal.TrailingObserverCount == 1 && scenario.Journal.TrailingObserverSawCallbackFailure
            && scenario.Journal.TrailingObservations.Single().AdapterCallbackAttemptsAtArrival == 1
            && scenario.Journal.TrailingObservations.Single().AdapterCallbackFailedBeforeArrival,
        "The observer registered after the adapter did not receive the completion after the callback failure.");

    var catalog = scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>();
    var catalogAfterFailure = await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "catalog after failed callback");
    var unchanged = await scenario.ReconcileAsync("changed-or-pending-unchanged-retry");
    Require(unchanged.ChangeSet.Added.Count + unchanged.ChangeSet.Updated.Count + unchanged.ChangeSet.Removed.Count == 0,
        "The pending retry must be driven by a later unchanged reconciliation completion.");
    Require(scenario.Journal.TrailingObserverCount == 2 && scenario.Journal.TrailingObservations[1].AppliedCount > 0,
        "The real Nuplane pipeline did not deliver the unchanged completion with applied active packages.");
    var afterRetry = registry.GetActive(shellName) ?? throw new InvalidOperationException("Pending retry left no shell active.");
    Require(afterRetry.Descriptor.Generation > afterFailedCallback.Descriptor.Generation,
        "ChangedOrPending did not retry the callback-failed reload on a later unchanged eligible completion.");
    Require(afterRetry.ServiceProvider.GetService(markerType) is not null,
        "The retry generation did not retain the currently active fixture feature.");
    Require(scenario.Journal.ReloadCallbackAttempts == 2 && !scenario.Journal.LastReloadCallbackFailed,
        "The callback-failed reload epoch was not retried successfully.");
    var catalogAfterRetry = await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "catalog after pending retry");
    Require(catalogAfterRetry.Generation == catalogAfterFailure.Generation,
        "A pending callback retry with no package changes unexpectedly refreshed the catalog.");

    var successfulRetryShellGeneration = afterRetry.Descriptor.Generation;
    var successfulRetryCatalogGeneration = catalogAfterRetry.Generation;
    var callbacksAfterSuccessfulRetry = scenario.Journal.ReloadCallbackAttempts;
    var postRetryUnchanged = await scenario.ReconcileAsync("changed-or-pending-after-success-unchanged");
    Require(postRetryUnchanged.ChangeSet.Added.Count + postRetryUnchanged.ChangeSet.Updated.Count + postRetryUnchanged.ChangeSet.Removed.Count == 0,
        "The post-retry check must be another real unchanged reconciliation.");
    Require(scenario.Journal.TrailingObserverCount == 3 && scenario.Journal.TrailingObservations[2].AppliedCount > 0,
        "Nuplane did not deliver a later unchanged completion after pending work succeeded.");
    var afterSettledUnchanged = registry.GetActive(shellName) ?? throw new InvalidOperationException("The settled unchanged cycle left no active shell.");
    var catalogAfterSettledUnchanged = await scenario.AwaitOperationAsync(catalog.GetSnapshotAsync(scenario.Token), "catalog after settled unchanged cycle");
    Require(catalogAfterSettledUnchanged.Generation == successfulRetryCatalogGeneration,
        "ChangedOrPending refreshed the catalog after the pending work had already succeeded.");
    Require(afterSettledUnchanged.Descriptor.Generation == successfulRetryShellGeneration,
        "ChangedOrPending reloaded again after the pending work had already succeeded.");
    Require(scenario.Journal.ReloadCallbackAttempts == callbacksAfterSuccessfulRetry,
        "ChangedOrPending invoked the reload callback after the pending work had already succeeded.");

    scenario.RemoveFixtureFromFeed();
    var finalRemoval = await scenario.ReconcileAsync("changed-or-pending-final-removal");
    Require(finalRemoval.ChangeSet.Removed.Contains(fixtureId, StringComparer.OrdinalIgnoreCase), "The retry scenario did not remove its final package.");
    var finalShell = registry.GetActive(shellName) ?? throw new InvalidOperationException("Final removal left no shell active.");
    Require(finalShell.Descriptor.Generation > afterSettledUnchanged.Descriptor.Generation, "Final removal did not auto-reload the changed-or-pending host.");
    Require(finalShell.ServiceProvider.GetService(markerType) is null, "Final removal generation still registered the fixture marker.");
    Require(scenario.Journal.ReloadCallbackAttempts == 3 && scenario.Journal.TrailingObserverCount == 4,
        "Expected first add, failed-cycle retry, settled unchanged cycle, and final removal to reach the trailing observer, with no extra adapter callback after retry succeeds.");

    scenario.WriteResult("pendingReloadRetry", new
    {
        RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending,
        AutoReload = true,
        firstGeneration,
        generationAfterFirstAdd = afterFailedCallback.Descriptor.Generation,
        generationAfterUnchangedRetry = afterRetry.Descriptor.Generation,
        generationAfterPostRetryUnchanged = afterSettledUnchanged.Descriptor.Generation,
        finalGeneration = finalShell.Descriptor.Generation,
        unchangedChangeSetWasEmpty = true,
        appliedPackagesCountOnUnchangedRetry = scenario.Journal.TrailingObservations[1].AppliedCount,
        catalogGenerationStayedStableAcrossRetry = catalogAfterRetry.Generation == catalogAfterFailure.Generation,
        postRetryUnchangedAppliedPackages = scenario.Journal.TrailingObservations[2].AppliedCount,
        settledUnchangedCycleDidNotRefreshOrReload = true,
        callbackFailedAfterSuccessfulReload = scenario.Journal.ReloadObservations[0].DeliberateFailure,
        observerAfterAdapterReceivedFailureCycle = scenario.Journal.TrailingObservations[0].AdapterCallbackFailedBeforeArrival,
        callbackRetriedAndSucceeded = !scenario.Journal.LastReloadCallbackFailed,
        finalCatalogAndGenerationOmitFixture = true,
        reloadCallbackObservations = scenario.Journal.ReloadObservations,
        trailingObserverObservations = scenario.Journal.TrailingObservations
    });
});

await RunScenarioAsync(consumerInputs, "workbench-manual-reload", fixtureInitiallyPresent: true, failFirstReloadCallback: false, configure: options =>
{
    options.RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending;
    options.AutoReload = false;
}, async scenario =>
{
    var registry = scenario.Provider.GetRequiredService<IShellRegistry>();
    _ = await scenario.ReconcileAsync("workbench-deferred-package-add");
    var active = await scenario.AwaitOperationAsync(registry.GetOrActivateAsync(shellName, scenario.Token), "Workbench shell activation");
    var markerType = await scenario.RequireFixtureBuiltAsync(active);
    var originalGeneration = active.Descriptor.Generation;
    var initialCatalog = await scenario.AwaitOperationAsync(scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(scenario.Token), "Workbench initial catalog snapshot");
    Require(initialCatalog.FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId), "Workbench profile initial catalog omitted the installed feature.");

    scenario.RemoveFixtureFromFeed();
    var removal = await scenario.ReconcileAsync("workbench-final-removal");
    Require(removal.ChangeSet.Removed.Contains(fixtureId, StringComparer.OrdinalIgnoreCase), "Workbench profile did not reconcile the last package removal.");
    var refreshedCatalog = await scenario.AwaitOperationAsync(scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(scenario.Token), "Workbench refreshed catalog snapshot");
    Require(refreshedCatalog.Generation > initialCatalog.Generation, "Workbench did not refresh its runtime catalog on changed package work.");
    Require(!refreshedCatalog.FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId), "Workbench refresh retained the removed feature in its catalog.");
    Require(registry.GetActive(shellName)?.Descriptor.Generation == originalGeneration,
        "Workbench profile unexpectedly auto-reloaded despite AutoReload=false.");
    Require(scenario.Journal.ReloadCallbackAttempts == 0, "Reload-off should still refresh without invoking reload-result reporting.");

    var manual = await scenario.AwaitOperationAsync(registry.ReloadAsync(shellName, scenario.Token), "Workbench manual reload");
    Require(manual.Error is null && manual.NewShell is not null, "Workbench's explicit manual reload did not succeed.");
    Require(manual.NewShell.Descriptor.Generation > originalGeneration, "The manual reload did not publish a new generation.");
    Require(manual.NewShell.ServiceProvider.GetService(markerType) is null,
        "The manually reloaded generation still contains the removed package feature service.");
    Require(!(await scenario.AwaitOperationAsync(scenario.Provider.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(scenario.Token), "Workbench final catalog snapshot"))
        .FeatureDescriptors.Any(feature => feature.Id == fixtureFeatureId), "The Workbench final generation did not use the empty refreshed catalog.");

    scenario.WriteResult("workbenchProfile", new
    {
        RefreshTrigger = NuplaneRefreshTrigger.ChangedOrPending,
        AutoReload = false,
        originalGeneration,
        generationAfterReconcile = originalGeneration,
        generationAfterManualReload = manual.NewShell.Descriptor.Generation,
        catalogAdvancedOnRemoval = refreshedCatalog.Generation > initialCatalog.Generation,
        noAutomaticReload = true,
        manualReloadUsedEmptyCatalog = true
    });
});

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    cshellsFamilyPackageVersion = expectedAdapterVersion,
    adapterPackageVersion = expectedAdapterVersion,
    nuplanePackageVersion,
    cshellsArchiveDirectory,
    nuplaneArchiveDirectory,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    targetFramework,
    loadedPackageAssets = new[] { adapterAssemblyEvidence, cshellsAssemblyEvidence, sharedAssemblyEvidence, nuplaneAssemblyEvidence, nuplaneLoadingAssemblyEvidence, nuplaneLoadingAbstractionsEvidence, nuplaneDirectoryAssemblyEvidence },
    qualifiedScope = "real Nuplane producer/autoload -> adapter -> catalog -> shell construction/reload and final removal-to-zero; no package-use lease or old-generation readability claim",
    scenarios = ScenarioResults.All
}, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));

static ServiceProvider ComposeDocumentedQuickstart()
{
    IServiceCollection services = new ServiceCollection();
    services.AddNuplane(nuplane => nuplane.AutoloadPackages());
    var shells = services.AddCShells();
    shells.WithNuplaneFeatureDiscovery();
    return services.BuildServiceProvider();
}

static async Task VerifyLateReloadDrainCaptureCleanupAsync(ConsumerInputs inputs)
{
    var scenario = new ConsumerScenario(inputs, "late-reload-drain-capture", fixtureInitiallyPresent: false, failFirstReloadCallback: false, configure: null);
    var drain = new GatedDrainOperation(ShellDescriptor.Create(shellName, 1));
    var reloadCompletion = new TaskCompletionSource<ReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var releaseReloadOnCancellation = scenario.Token.Register(() =>
        reloadCompletion.TrySetResult(new ReloadResult(shellName, new TestShell(drain.Descriptor), drain, null)));

    Exception? failure = null;
    var cleanupErrors = new List<Exception>();
    Task<IReadOnlyList<Exception>>? cleanupTask = null;
    try
    {
        scenario.Build();
        var registry = scenario.Provider.GetRequiredService<IShellRegistry>();
        Require(registry.GetAll(shellName).Count == 0,
            "The late-result self-check must begin cleanup with no shell generation retained in the registry.");
        try
        {
            _ = await scenario.AwaitOperationAsync(reloadCompletion.Task, "gated late manual reload result", TimeSpan.FromMilliseconds(75));
            throw new InvalidOperationException("The gated reload unexpectedly completed before its caller wait timed out.");
        }
        catch (TimeoutException)
        {
            // The bounded caller wait must time out first; CleanupAsync then cancels the
            // scenario and joins the still-owned reload task, whose result contains a drain.
        }

        cleanupTask = scenario.CleanupAsync();
        await drain.WaitEntered.WaitAsync(TimeSpan.FromSeconds(5));
        Require(registry.GetAll(shellName).Count == 0,
            "The synthetic late reload result should be the only retained drain while the registry has no generations.");
        Require(scenario.Journal.Drains.Count == 1 && ReferenceEquals(scenario.Journal.Drains[0], drain),
            "Cleanup did not capture the drain returned after the caller's reload wait timed out.");
        Require(Directory.Exists(scenario.RunRootPath) && !cleanupTask.IsCompleted,
            "Cleanup removed the owned run root or completed before joining the late reload drain.");

        drain.Complete();
        var errors = await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
        Require(errors.Count == 0, "The late-result cleanup self-check returned cleanup errors.");
        Require(!Directory.Exists(scenario.RunRootPath), "Cleanup did not remove the owned root after joining the late reload drain.");
    }
    catch (Exception exception)
    {
        failure = exception;
    }
    finally
    {
        drain.Complete();
        cleanupTask ??= scenario.CleanupAsync();
        try
        {
            cleanupErrors.AddRange(await cleanupTask.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        catch (Exception exception)
        {
            cleanupErrors.Add(exception);
        }
    }

    if (failure is not null && cleanupErrors.Count > 0)
        throw new AggregateException("Late reload-drain capture regression and its cleanup both failed.", [failure, .. cleanupErrors]);
    if (failure is not null)
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    if (cleanupErrors.Count > 0)
        throw new AggregateException("Late reload-drain capture regression cleanup failed.", cleanupErrors);
}

static async Task RunScenarioAsync(ConsumerInputs inputs, string name, bool fixtureInitiallyPresent, bool failFirstReloadCallback, Action<NuplaneIntegrationOptions>? configure, Func<ConsumerScenario, Task> body)
{
    var scenario = new ConsumerScenario(inputs, name, fixtureInitiallyPresent, failFirstReloadCallback, configure);
    Exception? scenarioError = null;
    var cleanupErrors = new List<Exception>();
    try
    {
        scenario.Build();
        await body(scenario);
    }
    catch (Exception exception)
    {
        scenarioError = exception;
    }
    finally
    {
        cleanupErrors.AddRange(await scenario.CleanupAsync());
    }

    if (scenarioError is not null && cleanupErrors.Count > 0)
        throw new AggregateException($"Scenario '{name}' failed and cleanup also failed.", [scenarioError, .. cleanupErrors]);
    if (scenarioError is not null)
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(scenarioError).Throw();
    if (cleanupErrors.Count > 0)
        throw new AggregateException($"Scenario '{name}' cleanup failed.", cleanupErrors);
}

sealed record ConsumerInputs(string ArtifactRoot, string FixtureArchiveDirectory);

sealed class ConsumerScenario
{
    private const string fixtureId = "ConsumerFixture.ShellFeature";
    private const string fixtureVersion = "1.0.0-preview.1";
    private const string fixtureFeatureId = "consumer-fixture-shell-feature";
    private const string sharedPackageId = "CShells.Abstractions";
    private const string shellName = "consumer-shell";
    private readonly string artifactRoot;
    private readonly string fixtureArchive;
    private readonly string fixtureSharedArchive;
    private readonly string runRoot;
    private readonly string feedRoot;
    private readonly string installRoot;
    private readonly string stateRoot;
    private readonly string stateFile;
    private readonly string packageLockFile;
    private readonly string fixturePath;
    private readonly EventJournal journal;
    private readonly object operationGate = new();
    private readonly List<OwnedOperation> ownedOperations = [];
    private ServiceProvider? provider;
    private readonly CancellationTokenSource timeout;
    private static readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan cleanupTimeout = TimeSpan.FromSeconds(15);

    public ConsumerScenario(ConsumerInputs inputs, string name, bool fixtureInitiallyPresent, bool failFirstReloadCallback, Action<NuplaneIntegrationOptions>? configure)
    {
        artifactRoot = inputs.ArtifactRoot;
        fixtureArchive = ArchiveFiles.Find(inputs.FixtureArchiveDirectory, $"{fixtureId}.{fixtureVersion}.nupkg");
        fixtureSharedArchive = ArchiveFiles.Find(inputs.FixtureArchiveDirectory, $"{sharedPackageId}.0.0.30-preview.165.nupkg");
        runRoot = Path.Combine(artifactRoot, "runs", $"{name}-{Guid.NewGuid():N}");
        feedRoot = Path.Combine(runRoot, "feed");
        installRoot = Path.Combine(runRoot, "install");
        stateRoot = Path.Combine(runRoot, "state");
        stateFile = Path.Combine(stateRoot, "state.json");
        packageLockFile = Path.Combine(stateRoot, "packages.lock.json");
        fixturePath = Path.Combine(feedRoot, $"{fixtureId}.{fixtureVersion}.nupkg");
        journal = new EventJournal(failFirstReloadCallback);
        timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        this.fixtureInitiallyPresent = fixtureInitiallyPresent;
        this.configure = configure;
    }

    private readonly bool fixtureInitiallyPresent;
    private readonly Action<NuplaneIntegrationOptions>? configure;

    public ServiceProvider Provider => provider ?? throw new InvalidOperationException("Scenario root provider has not been built.");
    public EventJournal Journal => journal;
    public CancellationToken Token => timeout.Token;
    public string RunRootPath => runRoot;

    public void Build()
    {
        Directory.CreateDirectory(feedRoot);
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(stateRoot);
        File.Copy(fixtureSharedArchive, Path.Combine(feedRoot, Path.GetFileName(fixtureSharedArchive)));
        if (fixtureInitiallyPresent)
            AddFixtureToFeed();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nuplane:Reconciliation:EnableAutomaticReconciliation"] = "false",
                ["Nuplane:Reconciliation:EnableStoreLock"] = "true",
                ["Nuplane:FeedResolution:OfflineMode"] = "true",
                ["Nuplane:FeedResolution:PackageInstallRoot"] = installRoot,
                ["Nuplane:FeedResolution:VersionCacheTtl"] = "00:00:00",
                ["Nuplane:FeedResolution:PackageBaseAddressCacheTtl"] = "00:00:00",
                ["Nuplane:LockFile:Path"] = packageLockFile,
                ["Nuplane:StoreRegistry:StateFilePath"] = stateFile
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(journal);
        services.AddNuplane(configuration.GetSection("Nuplane"), nuplane =>
        {
            nuplane.AddDirectoryFeed("consumer-fixture-feed", feedRoot, feed =>
            {
                feed.Include(fixtureId);
                feed.Watch = false;
            });
            nuplane.AutoloadPackages(loading => loading.SharedAssembly("CShells.Abstractions", publicKeyToken: null, majorVersion: 0));
        });

        var shells = services.AddCShells();
        shells.AddShell(shellName, shell => shell.WithFeature(fixtureFeatureId));
        shells.WithNuplaneFeatureDiscovery(options =>
        {
            configure?.Invoke(options);
            options.OnReloadResults = journal.RecordReloadResultsAsync;
        });
        // This service is deliberately registered after the adapter observer so the real
        // dispatcher must continue to it when the adapter's result callback throws.
        services.AddSingleton<INuplaneObserver, TrailingObserver>();
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var effectiveStore = Provider.GetRequiredService<EffectiveStorePersistenceSettings>();
        var reconciliationOptions = Provider.GetRequiredService<IOptions<ReconciliationOptions>>().Value;
        var feedOptions = Provider.GetRequiredService<IOptions<FeedResolutionOptions>>().Value;
        Require(effectiveStore.Mode == StorePersistenceMode.ConfiguredPath, "The scenario must use a persistent state file.");
        Require(Path.GetFullPath(effectiveStore.ResolvedStateFilePath!) == Path.GetFullPath(stateFile), "Nuplane resolved an unexpected state file.");
        Require(reconciliationOptions.EnableStoreLock && !reconciliationOptions.EnableAutomaticReconciliation, "Store locking must be enabled and background reconciliation disabled.");
        Require(Path.GetFullPath(feedOptions.PackageInstallRoot!) == Path.GetFullPath(installRoot) && feedOptions.OfflineMode, "Nuplane resolved unexpected feed settings.");
        Require(feedOptions.Feeds.Count == 1 && feedOptions.Feeds[0].Name == "consumer-fixture-feed", "Only the owned fixture directory feed may be enabled.");
    }

    public async Task<ReconciliationRunResult> ReconcileAsync(string reason)
    {
        var result = await AwaitOperationAsync(
            Provider.GetRequiredService<IReconciliationService>()
                .TriggerAsync(ReconciliationTrigger.Manual(reason), Token),
            $"reconciliation '{reason}'");
        Require(!result.Skipped && result.SkipReason == ReconciliationSkipReason.None, $"Reconciliation '{reason}' was skipped: {result.SkipReason}.");
        Require(!result.IsDegraded && result.FailedPackages.Count == 0, $"Reconciliation '{reason}' was degraded or had package failures.");
        return result;
    }

    public async Task<T> AwaitOperationAsync<T>(Task<T> operation, string description, TimeSpan? waitBound = null)
    {
        var owned = RegisterOperation(operation, description, result =>
        {
            if (result is ReloadResult { Drain: { } drain })
                journal.RecordDrain(drain);
        });
        try
        {
            var result = await operation.WaitAsync(waitBound ?? operationTimeout);
            owned.Capture(result);
            owned.TerminalOutcomeObserved = true;
            return result;
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"Timed out waiting for {description}; cleanup will cancel and join the owned operation.", exception);
        }
        catch
        {
            if (operation.IsCompleted && !operation.IsCompletedSuccessfully)
                owned.TerminalOutcomeObserved = true;
            throw;
        }
    }

    public void AddFixtureToFeed()
    {
        if (!File.Exists(fixturePath))
            File.Copy(fixtureArchive, fixturePath);
    }

    public void RemoveFixtureFromFeed()
    {
        if (File.Exists(fixturePath))
            File.Delete(fixturePath);
    }

    public async Task<Type> RequireFixtureBuiltAsync(IShell shell)
    {
        var catalog = Provider.GetRequiredService<IPackageAssemblyCatalog>();
        var package = await AwaitOperationAsync(catalog.GetPackagedAssembliesAsync(fixtureId, Token), "fixture package assembly discovery")
            ?? throw new InvalidOperationException("Nuplane's public package assembly catalog omitted the active fixture.");
        Require(package.Version == fixtureVersion, $"Unexpected loaded fixture version {package.Version}.");
        var assembly = package.Assemblies.Single(candidate => candidate.GetName().Name == fixtureId);
        var featureType = assembly.GetType($"{fixtureId}.ConsumerFixtureFeature", throwOnError: true)!;
        Require(typeof(IShellFeature).IsAssignableFrom(featureType)
                && featureType.GetInterfaces().Any(type => ReferenceEquals(type, typeof(IShellFeature))),
            "The loaded feature does not implement the consumer host's exact CShells.Abstractions interface.");

        var expectedTfm = $"net{Environment.Version.Major}.0";
        _ = ConsumerChecks.VerifyPackageAssembly(assembly, fixtureArchive, expectedTfm);
        var markerType = assembly.GetType($"{fixtureId}.ConsumerFixtureMarker", throwOnError: true)!;
        Require(shell.ServiceProvider.GetService(markerType) is not null,
            "The real shell did not construct/configure the package feature and register its marker service.");
        return markerType;
    }

    public void WriteResult(string name, object value) => ScenarioResults.Add(name, value);

    public async Task<IReadOnlyList<Exception>> CleanupAsync()
    {
        var errors = new List<Exception>();
        var allOperationsJoined = true;
        var allShellsDrained = true;
        var providerDisposed = provider is null;
        try { timeout.Cancel(); }
        catch (Exception exception) { errors.Add(exception); }

        try { allOperationsJoined = await JoinOwnedOperationsAsync(errors); }
        catch (Exception exception)
        {
            allOperationsJoined = false;
            errors.Add(new InvalidOperationException("Could not join all tracked scenario operations.", exception));
        }
        try { allShellsDrained = await DrainAllShellGenerationsAsync(errors); }
        catch (Exception exception)
        {
            allShellsDrained = false;
            errors.Add(new InvalidOperationException("Could not drain all retained shell generations.", exception));
        }

        if (provider is not null)
        {
            try
            {
                var disposeTask = provider.DisposeAsync().AsTask();
                await disposeTask.WaitAsync(cleanupTimeout);
                providerDisposed = true;
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException("Root service-provider disposal did not complete within the cleanup bound.", exception));
            }
        }
        if (allOperationsJoined && providerDisposed)
        {
            try { timeout.Dispose(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        if (allOperationsJoined && allShellsDrained && providerDisposed)
        {
            try
            {
                if (Directory.Exists(runRoot))
                    Directory.Delete(runRoot, recursive: true);
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        else
        {
            errors.Add(new IOException($"Retained owned scenario root '{runRoot}' because one or more operations, shell generations, or provider disposal did not reach a safe terminal state."));
        }
        return errors;
    }

    private OwnedOperation<T> RegisterOperation<T>(Task<T> operation, string description, Action<T> capture)
    {
        var owned = new OwnedOperation<T>(operation, description, capture);
        lock (operationGate)
            ownedOperations.Add(owned);
        return owned;
    }

    private async Task<bool> JoinOwnedOperationsAsync(List<Exception> errors)
    {
        OwnedOperation[] snapshot;
        lock (operationGate)
            snapshot = ownedOperations.ToArray();

        var allJoined = true;
        foreach (var operation in snapshot)
        {
            if (operation.TerminalOutcomeObserved)
                continue;

            try
            {
                await operation.JoinAndCaptureAsync().WaitAsync(cleanupTimeout);
                operation.TerminalOutcomeObserved = true;
            }
            catch (OperationCanceledException) when (operation.Task.IsCanceled)
            {
                operation.TerminalOutcomeObserved = true;
            }
            catch (TimeoutException exception)
            {
                allJoined = false;
                errors.Add(new TimeoutException($"Owned operation '{operation.Description}' did not stop after scenario cancellation; its run root will be retained.", exception));
            }
            catch (Exception exception)
            {
                operation.TerminalOutcomeObserved = true;
                errors.Add(new InvalidOperationException($"Owned operation '{operation.Description}' failed while cleanup joined it.", exception));
            }
        }

        return allJoined;
    }

    private async Task<bool> DrainAllShellGenerationsAsync(List<Exception> errors)
    {
        var allDrained = true;
        var drains = new HashSet<IDrainOperation>(journal.Drains, ReferenceEqualityComparer.Instance);
        if (provider is not null)
        {
            var registry = Provider.GetRequiredService<IShellRegistry>();
            foreach (var shell in registry.GetAll(shellName).ToArray())
            {
                try
                {
                    var drain = await registry.DrainAsync(shell, CancellationToken.None).WaitAsync(cleanupTimeout);
                    drains.Add(drain);
                }
                catch (Exception exception)
                {
                    allDrained = false;
                    errors.Add(new InvalidOperationException($"Could not start bounded drain for shell generation {shell.Descriptor.Generation}.", exception));
                }
            }
        }

        // Disposed notifications can remove old generations from GetAll before provider teardown finishes.
        // Join the reload-returned drain handles too, including callbacks that failed after committing.
        foreach (var drain in drains)
        {
            try
            {
                DrainResult result;
                try
                {
                    result = await drain.WaitAsync(CancellationToken.None).WaitAsync(cleanupTimeout);
                }
                catch (TimeoutException timeoutException)
                {
                    allDrained = false;
                    errors.Add(new TimeoutException("A shell drain exceeded the cleanup bound.", timeoutException));
                    await drain.ForceAsync(CancellationToken.None).WaitAsync(cleanupTimeout);
                    result = await drain.WaitAsync(CancellationToken.None).WaitAsync(cleanupTimeout);
                }

                if (result.Status != DrainStatus.Completed)
                {
                    allDrained = false;
                    errors.Add(new InvalidOperationException($"Shell generation {result.Shell.Generation} ended drain with status {result.Status}; cleanup requires a completed drain."));
                }
                foreach (var outcome in result.HandlerResults.Select(handler => (handler.Completed, handler.Error))
                    .Concat(result.TerminatorResults.Select(terminator => (terminator.Completed, terminator.Error))))
                {
                    if (!outcome.Completed || outcome.Error is not null)
                    {
                        allDrained = false;
                        errors.Add(new InvalidOperationException($"Shell generation {result.Shell.Generation} reported an incomplete or failed teardown participant.", outcome.Error));
                    }
                }
            }
            catch (Exception exception)
            {
                allDrained = false;
                errors.Add(new InvalidOperationException("A retained shell drain could not reach successful provider teardown.", exception));
            }
        }

        if (provider is not null)
        {
            var remaining = Provider.GetRequiredService<IShellRegistry>().GetAll(shellName).Select(shell => shell.Descriptor.Generation).ToArray();
            if (remaining.Length > 0)
            {
                allDrained = false;
                errors.Add(new InvalidOperationException($"Shell generations remain registered after bounded drain attempts: {string.Join(", ", remaining)}."));
            }
        }
        return allDrained;
    }

    private abstract class OwnedOperation(Task task, string description)
    {
        public Task Task { get; } = task;
        public string Description { get; } = description;
        public bool TerminalOutcomeObserved { get; set; }
        public abstract Task JoinAndCaptureAsync();
    }

    private sealed class OwnedOperation<T>(Task<T> task, string description, Action<T> capture)
        : OwnedOperation(task, description)
    {
        private int captured;

        public void Capture(T result)
        {
            if (Interlocked.Exchange(ref captured, 1) == 0)
                capture(result);
        }

        public override async Task JoinAndCaptureAsync()
        {
            Capture(await task);
        }
    }
}

sealed class EventJournal(bool failFirstReloadCallback)
{
    private int reloadCallbackAttempts;
    private int firstReloadCallbackFailed;
    private int lastReloadCallbackFailed;
    private int trailingObserverCount;
    private int trailingObserverSawCallbackFailure;
    private readonly HashSet<IDrainOperation> drains = new(ReferenceEqualityComparer.Instance);
    private readonly List<ReloadCallbackObservation> reloadObservations = [];
    private readonly List<TrailingObserverObservation> trailingObservations = [];
    private readonly object gate = new();

    public IReadOnlyList<IDrainOperation> Drains { get { lock (gate) return drains.ToArray(); } }

    public void RecordDrain(IDrainOperation drain)
    {
        lock (gate)
            drains.Add(drain);
    }

    public int ReloadCallbackAttempts => Volatile.Read(ref reloadCallbackAttempts);
    public bool FirstReloadCallbackFailed => Volatile.Read(ref firstReloadCallbackFailed) == 1;
    public bool LastReloadCallbackFailed => Volatile.Read(ref lastReloadCallbackFailed) == 1;
    public int TrailingObserverCount => Volatile.Read(ref trailingObserverCount);
    public bool TrailingObserverSawCallbackFailure => Volatile.Read(ref trailingObserverSawCallbackFailure) == 1;
    public IReadOnlyList<ReloadCallbackObservation> ReloadObservations { get { lock (gate) return reloadObservations.ToArray(); } }
    public IReadOnlyList<TrailingObserverObservation> TrailingObservations { get { lock (gate) return trailingObservations.ToArray(); } }

    public ValueTask RecordReloadResultsAsync(IReadOnlyList<ReloadResult> results, CancellationToken cancellationToken)
    {
        foreach (var result in results)
            if (result.Drain is { } drain)
                RecordDrain(drain);
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = Interlocked.Increment(ref reloadCallbackAttempts);
        var allReloadsSucceeded = results.Count > 0 && results.All(result => result.Error is null);
        var deliberateFailure = allReloadsSucceeded && failFirstReloadCallback && attempt == 1;
        var failed = !allReloadsSucceeded || deliberateFailure;
        Volatile.Write(ref lastReloadCallbackFailed, failed ? 1 : 0);
        if (failed)
            Volatile.Write(ref firstReloadCallbackFailed, 1);
        lock (gate)
            reloadObservations.Add(new ReloadCallbackObservation(attempt, results.Count, allReloadsSucceeded, deliberateFailure));
        if (!allReloadsSucceeded)
            throw new InvalidOperationException("Expected one successful automatic shell reload result.");
        if (deliberateFailure)
            throw new DeliberateReloadReportingFailureException();
        return ValueTask.CompletedTask;
    }

    public void RecordTrailingObserver(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages)
    {
        var count = Interlocked.Increment(ref trailingObserverCount);
        var adapterAttempts = ReloadCallbackAttempts;
        var adapterFailed = LastReloadCallbackFailed;
        if (adapterFailed)
            Volatile.Write(ref trailingObserverSawCallbackFailure, 1);
        lock (gate)
            trailingObservations.Add(new TrailingObserverObservation(count, changeSet.Added.Count, changeSet.Updated.Count, changeSet.Removed.Count, appliedPackages.Count, adapterAttempts, adapterFailed));
    }
}

sealed class TrailingObserver(EventJournal journal) : INuplaneObserver
{
    public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;
    public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;
    public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;
    public Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
    {
        journal.RecordTrailingObserver(changeSet, appliedPackages);
        return Task.CompletedTask;
    }
}

sealed class TestShell(ShellDescriptor descriptor) : IShell
{
    public ShellDescriptor Descriptor { get; } = descriptor;
    public ShellLifecycleState State => ShellLifecycleState.Active;
    public IServiceProvider ServiceProvider => EmptyServiceProvider.Instance;
    public IDrainOperation? Drain => null;
    public IShellScope BeginScope() => throw new NotSupportedException("The late-drain capture self-check does not open shell scopes.");
}

sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();
    public object? GetService(Type serviceType) => null;
}

sealed class GatedDrainOperation(ShellDescriptor descriptor) : IDrainOperation
{
    private readonly TaskCompletionSource<DrainResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int status = (int)DrainStatus.Pending;

    public DrainStatus Status => (DrainStatus)Volatile.Read(ref status);
    public DateTimeOffset? Deadline => null;
    public ShellDescriptor Descriptor { get; } = descriptor;
    public Task WaitEntered => entered.Task;

    public Task<DrainResult> WaitAsync(CancellationToken cancellationToken = default)
    {
        entered.TrySetResult();
        return completion.Task.WaitAsync(cancellationToken);
    }

    public Task ForceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Complete();
        return Task.CompletedTask;
    }

    public void Complete()
    {
        Volatile.Write(ref status, (int)DrainStatus.Completed);
        completion.TrySetResult(new DrainResult(
            Descriptor,
            DrainStatus.Completed,
            TimeSpan.Zero,
            0,
            []));
    }
}

sealed class DeliberateReloadReportingFailureException() : Exception("Intentional first callback failure for retry proof.");

sealed record AssemblyEvidence(string AssemblyFullName, string TargetFramework, string PackageAssetPath, string PackageAssetSha256, string LoadedSha256, string Location);
sealed record ReloadCallbackObservation(int Attempt, int ResultCount, bool EveryResultSucceeded, bool DeliberateFailure);
sealed record TrailingObserverObservation(int Count, int Added, int Updated, int Removed, int AppliedCount, int AdapterCallbackAttemptsAtArrival, bool AdapterCallbackFailedBeforeArrival);

static class ScenarioResults
{
    private static readonly Dictionary<string, object> Results = new(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string, object> All => Results;
    public static void Add(string name, object result) => Results.Add(name, result);
}

static class ArchiveFiles
{
    public static string Find(string directory, string expectedFileName)
    {
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Required audited archive '{expectedFileName}' was not found in '{directory}'."),
            _ => throw new InvalidOperationException($"Archive filename '{expectedFileName}' is ambiguous in '{directory}'.")
        };
    }
}

static class ConsumerChecks
{
    public static string ReadNuspecVersion(string archivePath, string expectedId)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var metadata = document.Root?.Element(ns + "metadata") ?? throw new InvalidOperationException("Package archive has no nuspec metadata.");
        Require(metadata.Element(ns + "id")?.Value == expectedId, $"Expected package ID {expectedId} in '{archivePath}'.");
        return metadata.Element(ns + "version")?.Value ?? throw new InvalidOperationException("Package nuspec has no version.");
    }

    public static AssemblyEvidence VerifyPackageAssembly(Assembly assembly, string archivePath, string targetFramework)
    {
        var expectedFramework = $".NETCoreApp,Version=v{Environment.Version.Major}.0";
        var actualFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
            ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' lacks TargetFrameworkAttribute.");
        Require(actualFramework == expectedFramework, $"Assembly '{assembly.FullName}' targets {actualFramework}; expected {expectedFramework}.");

        var simpleName = assembly.GetName().Name ?? throw new InvalidOperationException("Loaded assembly has no simple name.");
        var assetPath = $"lib/{targetFramework}/{simpleName}.dll";
        using var archive = ZipFile.OpenRead(archivePath);
        var asset = archive.GetEntry(assetPath) ?? throw new InvalidOperationException($"Archive '{archivePath}' lacks '{assetPath}'.");
        using var assetStream = asset.Open();
        var assetHash = Convert.ToHexString(SHA256.HashData(assetStream)).ToLowerInvariant();
        var loadedPath = assembly.Location;
        using var loadedStream = File.OpenRead(loadedPath);
        var loadedHash = Convert.ToHexString(SHA256.HashData(loadedStream)).ToLowerInvariant();
        Require(assetHash == loadedHash, $"Loaded assembly '{loadedPath}' differs from {assetPath} in '{archivePath}'.");
        return new AssemblyEvidence(assembly.FullName!, actualFramework, assetPath, assetHash, loadedHash, loadedPath);
    }

    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void RequireThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }
}
