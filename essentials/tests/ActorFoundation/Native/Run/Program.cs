using System.Diagnostics;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Native.Migration.SqliteFixture;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Run;
using Microsoft.EntityFrameworkCore;

// Usage:
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll smoke
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost sqlite
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost postgres "<connection string>"
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll memory [runs]

WriteLine.Out = null;
if (args is ["workbench-shipped-flowcharts", var graphProvider and ("sqlite" or "postgres"), var graphWorkbench])
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunShippedFlowchartsAsync(graphWorkbench, graphProvider == "postgres");
    return 0;
}
if (args is ["workbench-shipped-history", var historyProvider and ("sqlite" or "postgres"), var historyWorkbench, .. var historyMode] &&
    historyMode is ([] or ["playback"] or ["descendants"]))
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunShippedHistoryAsync(historyWorkbench, historyProvider == "postgres",
        historyMode.SingleOrDefault());
    return 0;
}
if (args is ["workbench-http-waits", var waitsProvider and ("sqlite" or "postgres"), var waitsWorkbench, .. var waitsMode] &&
    waitsMode is ([] or ["gated"]))
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunHttpWaitsAsync(waitsWorkbench, waitsProvider == "postgres",
        gated: waitsMode is ["gated"]);
    return 0;
}
if (args is ["workbench-shipped-children", var childrenProvider and ("sqlite" or "postgres"), var childrenWorkbench])
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunShippedChildrenAsync(childrenWorkbench, childrenProvider == "postgres");
    return 0;
}
if (args is ["workbench-terminal-output", var outputsProvider and ("sqlite" or "postgres"), var outputsWorkbench, .. var outputMode] &&
    outputMode is ([] or ["s6"] or ["shared"]))
{
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunTerminalOutputAsync(outputsWorkbench,
        outputsProvider == "postgres", withTimers: outputMode is ["s6"] or ["shared"], shared: outputMode is ["shared"]);
    return 0;
}
if (args is ["sqlite-workbench-core-preview", var workbench])
    return await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunSqliteAsync(workbench);
if (args is ["pg-workbench-core-preview", .. var workbenchPreview])
{
    var workbenchExecutable = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_WORKBENCH");
    if (string.IsNullOrWhiteSpace(workbenchExecutable) || !File.Exists(workbenchExecutable))
    {
        Console.Error.WriteLine("Set KERNEL_PREVIEW_WORKBENCH to the built Workbench executable.");
        return 2;
    }
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunAsync(workbenchPreview, workbenchExecutable);
    return 0;
}
if (args is ["pg-workbench-preview-reads-seed", var originalRoot, var seedFile, .. var readsProfile] && readsProfile is ([] or ["timed"]))
    return await PostgresCorePreviewReadSeed.RunAsync(originalRoot, seedFile, timed: readsProfile.Length != 0);
if (args is ["profile-capabilities"]) return ProfileCapabilityChecks.Run();
if (args is ["direct-test-run-profile", var testRunProfileProvider and ("postgres" or "sqlite")])
    return await ProfileCapabilityChecks.RunDirectTestRunStoreAsync(testRunProfileProvider == "postgres");
if (args is ["ephemeral-test-run-profile", var ephemeralTestRunProvider and ("postgres" or "sqlite")])
    return await ProfileCapabilityChecks.RunDirectTestRunStoreAsync(ephemeralTestRunProvider == "postgres", ephemeral: true);
if (args is ["qualified-inline-root-core"]) return await QualifiedInlineRootCoreChecks.RunAsync();
if (args is ["inline-root-promotion", var inlineProvider and ("postgres" or "sqlite"), var inlineFence and ("checked" or "in-batch")])
    return await InlineRootPromotionChecks.RunAsync(inlineProvider == "postgres", inlineFence == "in-batch");
if (args is ["inline-root-promotion", var lateInlineProvider and ("postgres" or "sqlite"), var lateInlineFence and ("checked" or "in-batch"), "125"])
    return await InlineRootPromotionChecks.RunAsync(lateInlineProvider == "postgres", lateInlineFence == "in-batch", laterRevision: true);
if (args is ["shipped-schedule-owner-mode", var modeProvider and ("postgres" or "sqlite"), var mode and ("standalone" or "managed")])
    return await ShippedScheduleOwnerModeChecks.RunAsync(modeProvider == "postgres", mode == "managed");
if (args is ["root-auxiliary-owner", var auxiliaryProvider and ("postgres" or "sqlite"), var auxiliaryFence and ("checked" or "in-batch")])
    return await RootAuxiliaryOwnerChecks.RunAsync(auxiliaryProvider == "postgres", auxiliaryFence == "in-batch");
if (args is ["root-schedule-controls", var controlsProvider and ("postgres" or "sqlite"), var controlsFence and ("checked" or "in-batch")])
    return await RootScheduleControlChecks.RunAsync(controlsProvider == "postgres", controlsFence == "in-batch");
if (args is ["root-managed-schedules", var managedProvider and ("postgres" or "sqlite"), var managedFence and ("checked" or "in-batch")])
    return await RootScheduleControlChecks.RunAsync(managedProvider == "postgres", managedFence == "in-batch", managedProfile: true);
if (args is ["root-schedule-inventory", var scheduleProvider and ("postgres" or "sqlite")])
    return await RootScheduleInventoryChecks.RunAsync(scheduleProvider == "postgres");
if (args is ["root-schedule-occurrence", var occurrenceProvider and ("postgres" or "sqlite")])
    return await RootScheduleInventoryChecks.RunAsync(occurrenceProvider == "postgres", settleOccurrences: true);
if (args is ["root-schedule-occurrence", var occurrenceLifecycleProvider and ("postgres" or "sqlite"), var occurrenceFence and ("checked" or "in-batch")])
    return await RootScheduleInventoryChecks.RunAsync(occurrenceLifecycleProvider == "postgres", settleOccurrences: true, fenceInBatch: occurrenceFence == "in-batch");
if (args is ["root-schedule-lineage", var lineageProvider and ("postgres" or "sqlite"), var lineageFence and ("checked" or "in-batch")])
    return await RootScheduleInventoryChecks.RunAsync(lineageProvider == "postgres", settleOccurrences: true,
        fenceInBatch: lineageFence == "in-batch", trackLineage: true);
if (args is ["pg-live-remote-http"]) return await PostgresLiveRemoteHttpChecks.RunAsync();
if (args is ["kernel-event-starts", var eventStartProvider and ("postgres" or "sqlite"), var eventStartFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunStartsAsync(eventStartProvider, eventStartFence == "in-batch");
if (args is ["kernel-event-receipts", var receiptProvider and ("postgres" or "sqlite"), var receiptFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunStartsAsync(receiptProvider, receiptFence == "in-batch", retainReceipts: true);
if (args is ["kernel-event-scan", var scanProvider and ("postgres" or "sqlite"), var scanFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunStartsAsync(scanProvider, scanFence == "in-batch", retainReceipts: true, orderedScan: true);
if (args is ["kernel-public-event-replies", var replyEventProvider and ("postgres" or "sqlite"), var replyEventFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunPublicEventRepliesAsync(replyEventProvider, replyEventFence == "in-batch");
if (args is ["kernel-public-event-remote-reply", var remoteEventFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunRemotePublicEventReplyAsync(remoteEventFence == "in-batch");
if (args is ["kernel-protected-event-remote-observer"])
    return await KernelAuthoredEventChecks.RunRemotePublicEventReplyAsync(inBatch: true, protectedObserver: true);
if (args is ["kernel-public-event-starts", var publicEventProvider and ("postgres" or "sqlite"), var publicEventFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunPublicStartsAsync(publicEventProvider, publicEventFence == "in-batch");
if (args is ["kernel-public-event-host", var eventHostProvider and ("postgres" or "sqlite"),
    var eventHostFence and ("checked" or "in-batch"), var eventHostMode and ("generic" or "protected")])
    return await KernelAuthoredEventChecks.RunPublicEventHostAsync(eventHostProvider, eventHostFence == "in-batch", eventHostMode == "protected");
if (args is ["kernel-protected-event-observer"])
    return await KernelAuthoredEventChecks.RunPublicEventHostAsync("postgres", inBatch: true, protectedStarts: true, observeReplies: true);
if (args is ["kernel-paged-event-publication", var pagedEventProvider and ("postgres" or "sqlite"), var pagedEventFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunPagedEventPublicationAsync(pagedEventProvider, pagedEventFence == "in-batch");
if (args is ["kernel-physical-event-pages", var eventPageProvider and ("postgres" or "sqlite"), var eventPageFence and ("checked" or "in-batch")])
    return await KernelAuthoredEventChecks.RunPagedEventPublicationAsync(eventPageProvider, eventPageFence == "in-batch", physicalPages: true);
