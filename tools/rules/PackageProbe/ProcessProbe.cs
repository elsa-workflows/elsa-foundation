using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

internal static partial class Program
{
    private const string ProcessConnectionEnvironmentVariable = "ELSA_RULES_PROCESS_CONNECTION";
    private const int MaxProcessStateBytes = 16 * 1024 * 1024;

    private static async Task<object> RunProcessBoundaryAsync(string boundary, string statePath)
    {
        Require(boundary is "accepted" or "consumed", "Process boundary must be accepted or consumed.");
        Require(Path.IsPathFullyQualified(statePath), "Process state path must be absolute.");
        var fullStatePath = Path.GetFullPath(statePath);
        var releasePath = fullStatePath + ".release";
        Require(!File.Exists(fullStatePath) && !File.Exists(releasePath), "Process boundary state or release marker already exists.");
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var control = new ProcessBoundaryControl(boundary, fullStatePath, releasePath, connection);
        using var host = BuildProcessHost(connection, control, suppressWorker: false);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var prepared = await services.GetRequiredService<IRuleInvocationFactory>().PrepareAsync(
                NewIdentity("process-" + boundary), Witness(), Facts("seed"), cancellationToken: timeout.Token);
            Require(prepared.Disposition == RuleInvocationPreparationDisposition.Prepared && prepared.Invocation is not null,
                $"Process invocation could not be prepared: {prepared.Disposition}.");
            control.Invocation = prepared.Invocation;
            var store = services.GetRequiredService<IRuleRunStore>();
            Require(store is ProcessBoundaryStore, "The process boundary store observer is not active.");
            var admission = await store.AdmitAsync(new RuleRunAdmission(prepared.Invocation), timeout.Token);
            Require(admission.Disposition == RuleAdmissionDisposition.Admitted,
                $"Process invocation was not admitted: {admission.Disposition}.");

            var state = await control.Captured.Task.WaitAsync(timeout.Token);
            Require(state.InvocationFingerprint == RuleRecordCodec.Fingerprint(prepared.Invocation).Sha256,
                "Committed boundary marker does not match the admitted invocation.");
            await WaitForReleaseAsync(releasePath, timeout.Token);
            var callback = await control.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(30), timeout.Token);
            var final = await ReadStableFinalAsync(services.GetRequiredService<IRuleRunReader>(), prepared.Invocation.Identity,
                timeout.Token);
            Require(final.View.Status is RuleRunStatus.Succeeded or RuleRunStatus.Cancelled,
                $"Original worker callback did not leave a stable terminal run: {final.View.Status}.");
            Require(callback == (boundary == "accepted" ? "Terminal" : "LeaseLost"),
                $"Original worker returned an unexpected post-boundary disposition: {callback}.");
            return new
            {
                command = "process-boundary",
                passed = true,
                boundary,
                processId = Environment.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = state.DatabaseEngine,
                databaseEngineVersion = state.DatabaseEngineVersion,
                databaseName = state.DatabaseName,
                invocationFingerprint = state.InvocationFingerprint,
                boundaryReceiptId = state.ReceiptId,
                boundaryAttemptId = state.AcceptedAttemptId,
                boundaryWorkerId = state.AcceptedWorkerId,
                boundaryLeaseFence = state.AcceptedLeaseFence,
                originalCallbackDisposition = callback,
                terminalBase64 = Convert.ToBase64String(RuleRecordCodec.TerminalBytes(final.Terminal)),
                modelRevision = final.View.ModelRevision,
                finalStatus = final.View.Status.ToString(),
                finalModelRevision = final.View.ModelRevision,
                finalCommittedRounds = final.View.CommittedRounds,
                finalFacts = final.Facts
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static async Task<object> RunProcessRecoverAsync(string statePath)
    {
        var state = ReadProcessState(statePath);
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var host = BuildProcessHost(connection, control: null, suppressWorker: false);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var invocation = state.Invocation;
            var reader = services.GetRequiredService<IRuleRunReader>();
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.DatabaseName == state.DatabaseName, "Recovery connected to a different PostgreSQL database.");
            Require(Environment.MachineName == state.MachineName, "Recovery ran on a different machine from the committed boundary.");
            Require(Environment.ProcessId != state.ProcessId, "Recovery did not run in an independent process.");

            // The successor deliberately does not prepare or admit. Its hosted worker claims the durable row itself.
            var view = await WaitForTerminalAsync(reader, invocation.Identity, timeout.Token);
            var terminal = await ReadFoundTerminalAsync(reader, invocation.Identity, timeout.Token);
            Require(view.Status == RuleRunStatus.Succeeded && terminal.Kind == RuleTerminalKind.Succeeded,
                "Recovered run did not succeed.");
            Require(view.CommittedRounds == 4 && terminal.CommittedRounds == 4,
                "Recovered run did not commit exactly four closure rounds.");
            var finalFacts = FactAtoms(terminal.ResultFacts);
            Require(finalFacts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal),
                "Recovered terminal facts differ from the deterministic closure.");
            Require(view.PendingReceiptId is null, "Recovered run retained a pending receipt.");
            var progress = await ReadProgressPageAsync(reader, invocation.Identity, timeout.Token);
            var rounds = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Round).Select(item => item.Round!)
                .OrderBy(round => round.Ordinal).ToArray();
            var facts = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Fact).Select(item => item.Fact!.Atom)
                .OrderBy(atom => atom, StringComparer.Ordinal).ToArray();
            Require(facts.SequenceEqual(ExpectedFinalFacts, StringComparer.Ordinal), "Recovered fact history differs from its terminal facts.");
            Require(rounds.Length == 4 && rounds.Select(round => round.Ordinal).SequenceEqual(Enumerable.Range(0, 4)),
                "Recovered round history is not exactly ordinals zero through three.");
            var first = rounds[0];
            Require(first.ReceiptId == state.ReceiptId && first.SemanticReceiptSha256 == state.SemanticReceiptSha256,
                "Recovery replaced the original committed receipt identity.");
            Require(first.AcceptedAttemptId == state.AcceptedAttemptId && first.AcceptedWorkerId == state.AcceptedWorkerId
                    && first.AcceptedLeaseFence == state.AcceptedLeaseFence,
                "Recovery changed the first physical accepted-attempt attribution.");
            Require(first.AcceptedModelRevision == state.AcceptedModelRevision && first.ConsumedModelRevision is not null,
                "Recovered first round did not preserve its acceptance and record its consumption.");
            Require(rounds.All(round => round.ConsumedModelRevision is not null
                    && !string.IsNullOrWhiteSpace(round.AcceptedAttemptId)
                    && !string.IsNullOrWhiteSpace(round.AcceptedWorkerId)
                    && round.AcceptedLeaseFence is > 0),
                "Recovered history contains a round without accepted and consumed attempt evidence.");
            var stable = await ReadStableFinalAsync(reader, invocation.Identity, timeout.Token);
            Require(stable.View.TerminalId == view.TerminalId && stable.View.ModelRevision == view.ModelRevision
                    && stable.Terminal.TerminalId == terminal.TerminalId,
                "Recovered terminal changed during the stability read.");
            return new
            {
                command = "process-recover",
                passed = true,
                recoveredBoundary = state.Boundary,
                terminalBase64 = Convert.ToBase64String(RuleRecordCodec.TerminalBytes(terminal)),
                modelRevision = view.ModelRevision,
                processId = Environment.ProcessId,
                originalProcessId = state.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                invocationFingerprint = state.InvocationFingerprint,
                status = view.Status.ToString(),
                committedRounds = view.CommittedRounds,
                finalFacts,
                roundHistory = rounds.Select(round => new
                {
                    ordinal = round.Ordinal,
                    receiptId = round.ReceiptId,
                    semanticReceiptSha256 = round.SemanticReceiptSha256,
                    acceptedAttemptId = round.AcceptedAttemptId,
                    acceptedWorkerId = round.AcceptedWorkerId,
                    acceptedLeaseFence = round.AcceptedLeaseFence,
                    acceptedModelRevision = round.AcceptedModelRevision,
                    consumedModelRevision = round.ConsumedModelRevision
                }).ToArray(),
                terminalId = terminal.TerminalId,
                terminalStabilityVerified = true,
                designStorageUsed = false
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static async Task<object> RunProcessCancelAsync(string statePath)
    {
        var state = ReadProcessState(statePath);
        Require(state.Boundary == "accepted", "Process cancellation requires an accepted-boundary marker.");
        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var host = BuildProcessHost(connection, control: null, suppressWorker: true);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var store = services.GetRequiredService<IRuleRunStore>();
            var reader = services.GetRequiredService<IRuleRunReader>();
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.DatabaseName == state.DatabaseName, "Cancellation connected to a different PostgreSQL database.");

            var acceptedBefore = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            Require(acceptedBefore.View.Status == RuleRunStatus.Running && acceptedBefore.View.ModelRevision == 2
                    && acceptedBefore.View.CommittedRounds == 0 && acceptedBefore.View.PendingReceiptId == state.ReceiptId,
                "Accepted boundary is not the expected running, unconsumed receipt state.");
            Require(acceptedBefore.Facts.SequenceEqual(["seed"], StringComparer.Ordinal),
                "Accepted boundary changed facts before receipt consumption.");
            Require(acceptedBefore.Rounds.Length == 1 && acceptedBefore.Rounds[0].Ordinal == 0
                    && acceptedBefore.Rounds[0].ReceiptId == state.ReceiptId
                    && acceptedBefore.Rounds[0].SemanticReceiptSha256 == state.SemanticReceiptSha256
                    && acceptedBefore.Rounds[0].ConsumedModelRevision is null
                    && acceptedBefore.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
                    && acceptedBefore.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
                    && acceptedBefore.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence,
                "Accepted boundary did not preserve its pending round and first attempt attribution.");
            var changedInvocation = WithMaxRounds(state.Invocation, state.Invocation.MaxRounds - 1);
            var changedCancel = await store.CancelAsync(new RuleCancellationRequest(changedInvocation), timeout.Token);
            Require(changedCancel.Disposition == RuleCancellationDisposition.Conflict
                    && changedCancel.ModelRevision is null && changedCancel.TerminalId is null,
                $"Changed-fingerprint cancellation returned {changedCancel.Disposition} instead of Conflict.");
            var afterConflict = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            Require(SameSnapshot(acceptedBefore, afterConflict), "Changed-fingerprint cancellation mutated the accepted run.");

            var cancelled = await store.CancelAsync(new RuleCancellationRequest(state.Invocation), timeout.Token);
            Require(cancelled.Disposition == RuleCancellationDisposition.Cancelled,
                $"Exact accepted-run cancellation returned {cancelled.Disposition}.");
            var acceptedAfter = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token);
            Require(acceptedAfter.View.Status == RuleRunStatus.Cancelled && acceptedAfter.View.CommittedRounds == 0
                    && acceptedAfter.View.CommittedFactCount == 1 && acceptedAfter.View.PendingReceiptId is null
                    && acceptedAfter.Terminal is not null
                    && acceptedAfter.Terminal.Kind == RuleTerminalKind.Cancelled
                    && acceptedAfter.Terminal.CommittedRounds == 0
                    && acceptedAfter.Rounds.Length == 1 && acceptedAfter.Rounds[0].Ordinal == 0
                    && acceptedAfter.Rounds[0].ReceiptId == state.ReceiptId
                    && acceptedAfter.Rounds[0].ConsumedModelRevision is null
                    && acceptedAfter.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
                    && acceptedAfter.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
                    && acceptedAfter.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence
                    && acceptedAfter.Facts.SequenceEqual(["seed"], StringComparer.Ordinal)
                    && acceptedAfter.TerminalFacts.SequenceEqual(["seed"], StringComparer.Ordinal),
                "Accepted-run cancellation changed committed facts or rounds.");
            var replay = await store.CancelAsync(new RuleCancellationRequest(state.Invocation), timeout.Token);
            Require(replay.Disposition == RuleCancellationDisposition.Replay,
                $"Exact accepted-run cancellation replay returned {replay.Disposition}.");
            Require(SameSnapshot(acceptedAfter, await ReadRunSnapshotAsync(reader, state.Invocation.Identity, timeout.Token)),
                "Accepted-run cancellation replay changed its stable terminal.");

            var prepared = await services.GetRequiredService<IRuleInvocationFactory>().PrepareAsync(
                NewIdentity("cancel-before-admission"), Witness(), Facts("seed"), cancellationToken: timeout.Token);
            Require(prepared.Disposition == RuleInvocationPreparationDisposition.Prepared && prepared.Invocation is not null,
                $"Cancellation tombstone invocation could not be prepared: {prepared.Disposition}.");
            var tombstone = prepared.Invocation;
            var beforeAdmissionCancel = await store.CancelAsync(new RuleCancellationRequest(tombstone), timeout.Token);
            Require(beforeAdmissionCancel.Disposition == RuleCancellationDisposition.Cancelled,
                $"Cancellation before admission returned {beforeAdmissionCancel.Disposition}.");
            var tombstoneAfterCancel = await ReadRunSnapshotAsync(reader, tombstone.Identity, timeout.Token);
            Require(tombstoneAfterCancel.View.Status == RuleRunStatus.Cancelled
                    && tombstoneAfterCancel.View.CommittedRounds == 0
                    && tombstoneAfterCancel.View.CommittedFactCount == 0
                    && tombstoneAfterCancel.View.PendingReceiptId is null
                    && tombstoneAfterCancel.Terminal is not null
                    && tombstoneAfterCancel.Terminal.Kind == RuleTerminalKind.Cancelled
                    && tombstoneAfterCancel.TerminalFacts.Length == 0
                    && tombstoneAfterCancel.Facts.Length == 0 && tombstoneAfterCancel.Rounds.Length == 0,
                "Cancellation-before-admission created facts, receipts, or rounds.");
            var cancelledAdmission = await store.AdmitAsync(new RuleRunAdmission(tombstone), timeout.Token);
            Require(cancelledAdmission.Disposition == RuleAdmissionDisposition.AlreadyCancelled,
                $"Admission after cancellation returned {cancelledAdmission.Disposition}.");
            Require(SameSnapshot(tombstoneAfterCancel, await ReadRunSnapshotAsync(reader, tombstone.Identity, timeout.Token)),
                "Admission after a cancellation tombstone changed durable state.");
            var changedTombstoneCancel = await store.CancelAsync(new RuleCancellationRequest(WithMaxRounds(tombstone, tombstone.MaxRounds - 1)), timeout.Token);
            Require(changedTombstoneCancel.Disposition == RuleCancellationDisposition.Conflict,
                $"Changed-fingerprint tombstone cancellation returned {changedTombstoneCancel.Disposition}.");
            Require(SameSnapshot(tombstoneAfterCancel, await ReadRunSnapshotAsync(reader, tombstone.Identity, timeout.Token)),
                "Changed-fingerprint tombstone cancellation changed durable state.");

            return new
            {
                command = "process-cancel",
                passed = true,
                terminalBase64 = Convert.ToBase64String(RuleRecordCodec.TerminalBytes(acceptedAfter.Terminal!)),
                modelRevision = acceptedAfter.View.ModelRevision,
                processId = Environment.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                acceptedRun = new
                {
                    invocationFingerprint = state.InvocationFingerprint,
                    changedFingerprint = changedCancel.Disposition.ToString(),
                    exactCancellation = cancelled.Disposition.ToString(),
                    exactReplay = replay.Disposition.ToString(),
                    status = acceptedAfter.View.Status.ToString(),
                    committedRounds = acceptedAfter.View.CommittedRounds,
                    committedFacts = acceptedAfter.Facts
                },
                cancellationBeforeAdmission = new
                {
                    invocationId = tombstone.Identity.InvocationId,
                    cancellation = beforeAdmissionCancel.Disposition.ToString(),
                    admission = cancelledAdmission.Disposition.ToString(),
                    changedFingerprint = changedTombstoneCancel.Disposition.ToString(),
                    status = tombstoneAfterCancel.View.Status.ToString(),
                    committedFacts = tombstoneAfterCancel.Facts,
                    receiptCount = tombstoneAfterCancel.Rounds.Length
                },
                workerSuppressed = true
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static IHost BuildProcessHost(string connection, ProcessBoundaryControl? control, bool suppressWorker)
    {
        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new ProbeAssertionException("Entry assembly is unavailable.");
        var hostRoot = ResolvePhysicalPath(AppContext.BaseDirectory);
        var depsPath = Path.ChangeExtension(entryAssembly.Location, ".deps.json");
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var runtimeFeature = new RulesRuntimeFeature
        {
            HostRoot = hostRoot,
            HostDepsPath = depsPath,
            DeploymentReceiptPath = Path.Combine(hostRoot, ReceiptFileName),
            MaxConcurrentAttempts = 1,
            PollIntervalMilliseconds = 10
        };
        var beforeRuntime = new HashSet<ServiceDescriptor>(builder.Services, ReferenceEqualityComparer.Instance);
        runtimeFeature.ConfigureServices(builder.Services);
        if (suppressWorker)
        {
            var workers = builder.Services.Where(descriptor => !beforeRuntime.Contains(descriptor)
                    && descriptor.ServiceType == typeof(IHostedService)).ToArray();
            Require(workers.Length == 1, "Could not identify exactly one Rules hosted worker for cancellation suppression.");
            foreach (var worker in workers)
                builder.Services.Remove(worker);
        }
        if (control is not null)
            builder.Services.AddSingleton(control);
        new RulesRuntimeEntityFrameworkCoreFeature
        {
            Provider = "PostgreSql",
            ConnectionString = connection,
            Pooling = false,
            WorkLeaseSeconds = 3,
            DeliveryLeaseSeconds = 3
        }.ConfigureServices(builder.Services);
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);

        if (control is not null)
        {
            var descriptor = builder.Services.LastOrDefault(item => item.ServiceType == typeof(IRuleRunStore));
            Require(descriptor is not null, "PostgreSQL EF Rules feature did not register IRuleRunStore.");
            builder.Services.Remove(descriptor);
            builder.Services.AddScoped<IRuleRunStore>(services =>
            {
                var inner = CreateFromDescriptor(descriptor, services) as IRuleRunStore
                    ?? throw new InvalidOperationException("The PostgreSQL Rules store descriptor did not create a store.");
                var reader = inner as IRuleRunReader
                    ?? throw new InvalidOperationException("The PostgreSQL Rules store does not implement its paired reader.");
                return new ProcessBoundaryStore(inner, reader, services.GetRequiredService<ProcessBoundaryControl>());
            });
        }

        return builder.Build();
    }

    private static object? CreateFromDescriptor(ServiceDescriptor descriptor, IServiceProvider services)
    {
        if (descriptor.ImplementationFactory is { } factory)
            return factory(services);
        if (descriptor.ImplementationType is { } implementationType)
            return ActivatorUtilities.CreateInstance(services, implementationType);
        return descriptor.ImplementationInstance;
    }

    private static async Task StartProcessHostAsync(IHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Provider exceptions can include endpoint details. Never serialize them into evidence.
            throw new ProbeAssertionException($"PostgreSQL Rules host startup failed ({error.GetType().Name}).");
        }
    }

    private static async Task StopProcessHostAsync(IHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ProbeAssertionException($"PostgreSQL Rules host shutdown failed ({error.GetType().Name}).");
        }
    }

    private static string RequiredProcessConnection()
    {
        var connection = Environment.GetEnvironmentVariable(ProcessConnectionEnvironmentVariable);
        Require(!string.IsNullOrWhiteSpace(connection), $"{ProcessConnectionEnvironmentVariable} is required.");
        return connection!;
    }

    private static async Task<PostgresProfile> ReadPostgresProfileAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "select current_database(), version()";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            Require(await reader.ReadAsync(cancellationToken), "PostgreSQL did not return its live database profile.");
            var databaseName = reader.GetString(0);
            var version = reader.GetString(1);
            Require(!string.IsNullOrWhiteSpace(databaseName) && !string.IsNullOrWhiteSpace(version),
                "PostgreSQL returned an empty database name or engine version.");
            return new("PostgreSQL", version, databaseName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProbeAssertionException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ProbeAssertionException($"Could not read PostgreSQL engine metadata ({error.GetType().Name}).");
        }
        finally
        {
            if (connection.State != System.Data.ConnectionState.Closed)
                await connection.CloseAsync();
        }
    }

    private static async Task<RuleTerminalResult> ReadFoundTerminalAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var terminal = await reader.ReadTerminalAsync(identity, cancellationToken);
        Require(terminal.Disposition == RuleTerminalReadDisposition.Found && terminal.Result is not null,
            $"Terminal read returned {terminal.Disposition}.");
        return terminal.Result!;
    }

    private static async Task<RuleProgressPage> ReadProgressPageAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var progress = await reader.ReadProgressAsync(identity, null, RuleLimits.MaxPageEntries, cancellationToken);
        Require(progress.Disposition == RuleProgressReadDisposition.Page && progress.Page is not null,
            $"Progress read returned {progress.Disposition}.");
        Require(progress.Page.Next is null, "Process witness history exceeded its bounded single page.");
        return progress.Page;
    }

    private static async Task<RunSnapshot> ReadRunSnapshotAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var lookup = await reader.FindAsync(identity, cancellationToken);
        Require(lookup.Disposition == RuleRunReadDisposition.Found && lookup.View is not null,
            $"Run snapshot returned {lookup.Disposition}.");
        var terminalResult = await reader.ReadTerminalAsync(identity, cancellationToken);
        Require(terminalResult.Disposition is RuleTerminalReadDisposition.Found or RuleTerminalReadDisposition.NotTerminal,
            $"Terminal snapshot returned {terminalResult.Disposition}.");
        var progress = await ReadProgressPageAsync(reader, identity, cancellationToken);
        var facts = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Fact).Select(item => item.Fact!.Atom)
            .OrderBy(atom => atom, StringComparer.Ordinal).ToArray();
        var rounds = progress.Items.Where(item => item.Kind == RuleProgressItemKind.Round).Select(item => item.Round!)
            .OrderBy(round => round.Ordinal).ToArray();
        var terminal = terminalResult.Result;
        return new(lookup.View!, terminal, facts, rounds,
            terminal is null ? Array.Empty<string>() : FactAtoms(terminal.ResultFacts));
    }

    private static bool SameSnapshot(RunSnapshot left, RunSnapshot right) =>
        left.View == right.View && TerminalsEqual(left.Terminal, right.Terminal)
        && left.Facts.SequenceEqual(right.Facts, StringComparer.Ordinal)
        && left.TerminalFacts.SequenceEqual(right.TerminalFacts, StringComparer.Ordinal)
        && left.Rounds.SequenceEqual(right.Rounds);

    private static async Task<StableFinal> ReadStableFinalAsync(IRuleRunReader reader, RuleRunIdentity identity,
        CancellationToken cancellationToken)
    {
        var first = await ReadRunSnapshotAsync(reader, identity, cancellationToken);
        await Task.Delay(50, cancellationToken);
        var second = await ReadRunSnapshotAsync(reader, identity, cancellationToken);
        Require(SameSnapshot(first, second), "Terminal run state changed during a stability read.");
        Require(first.Terminal is not null, "Stable terminal result is missing.");
        return new(first.View, first.Terminal!, first.Facts);
    }

    private static RuleInvocationEnvelope WithMaxRounds(RuleInvocationEnvelope invocation, int maxRounds) =>
        new(invocation.Identity, invocation.Artifact, invocation.InitialFacts, maxRounds,
            invocation.ExpectedImplementation, invocation.InvocationSchemaVersion, invocation.NativeDestination);

    private static async Task WaitForReleaseAsync(string releasePath, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        while (!File.Exists(releasePath))
            await Task.Delay(50, timeout.Token);
    }

    private static ProcessBoundaryState ReadProcessState(string statePath)
    {
        Require(Path.IsPathFullyQualified(statePath), "Process state path must be absolute.");
        var bytes = ReadBoundedFile(Path.GetFullPath(statePath), MaxProcessStateBytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "Process state must be a JSON object.");
        var state = JsonSerializer.Deserialize<ProcessBoundaryState>(bytes, JsonOptions)
            ?? throw new ProbeAssertionException("Process state could not be decoded.");
        Require(state.SchemaVersion == 1 && state.Boundary is "accepted" or "consumed",
            "Process state schema or boundary is unsupported.");
        Require(state.ProcessId > 0 && !string.IsNullOrWhiteSpace(state.MachineName)
                && !string.IsNullOrWhiteSpace(state.DatabaseName)
                && state.DatabaseEngine == "PostgreSQL" && !string.IsNullOrWhiteSpace(state.DatabaseEngineVersion),
            "Process state lacks valid process or database provenance.");
        Require(state.InvocationBase64.Length <= RuleLimits.MaxEnvelopeBytes * 2,
            "Process invocation exceeds its encoded byte bound.");
        byte[] invocationBytes;
        try { invocationBytes = Convert.FromBase64String(state.InvocationBase64); }
        catch (FormatException) { throw new ProbeAssertionException("Process invocation is not valid Base64."); }
        var invocation = RuleRecordCodec.ReadInvocation(invocationBytes);
        Require(RuleRecordCodec.InvocationBytes(invocation).AsSpan().SequenceEqual(invocationBytes),
            "Process invocation is not canonical.");
        var fingerprint = RuleRecordCodec.Fingerprint(invocation).Sha256;
        Require(StringComparer.Ordinal.Equals(fingerprint, state.InvocationFingerprint),
            "Process invocation fingerprint does not match its state marker.");
        Require(state.ReceiptRecordBase64.Length <= RuleLimits.MaxEnvelopeBytes * 2,
            "Process receipt exceeds its encoded byte bound.");
        byte[] receiptBytes;
        try { receiptBytes = Convert.FromBase64String(state.ReceiptRecordBase64); }
        catch (FormatException) { throw new ProbeAssertionException("Process receipt is not valid Base64."); }
        var receipt = RuleRecordCodec.ReadReceiptRecord(receiptBytes);
        Require(receipt.Identity.Run == invocation.Identity && receipt.Identity.Fingerprint == RuleRecordCodec.Fingerprint(invocation)
                && receipt.Identity.Ordinal == 0 && receipt.ImplementationPinHash == RuleRecordCodec.PinHash(invocation.ExpectedImplementation)
                && receipt.SemanticReceiptSha256 == state.SemanticReceiptSha256,
            "Process receipt does not match the canonical invocation and recorded semantic identity.");
        Require(receipt.Identity.InputStateSha256 == state.InputStateSha256
                && state.ReceiptId == ComputeReceiptId(receipt),
            "Process receipt does not match the recorded state identity.");
        Require(state.AcceptedModelRevision > 0 && state.AcceptedLeaseFence > 0
                && !string.IsNullOrWhiteSpace(state.AcceptedAttemptId)
                && !string.IsNullOrWhiteSpace(state.AcceptedWorkerId),
            "Process state lacks accepted attempt attribution.");
        Require(!string.IsNullOrWhiteSpace(state.TokenOwner) && state.TokenFence > 0
                && state.TokenExpiresAtUtc != default
                && state.TokenExpectedModelRevision == (state.Boundary == "accepted" ? 1 : 2),
            "Process state lacks the original fenced work token metadata.");
        var expectedAfter = state.Boundary == "accepted" ? new[] { "seed" } : new[] { "left", "right", "seed" };
        Require(state.AfterFacts.OrderBy(atom => atom, StringComparer.Ordinal)
                    .SequenceEqual(expectedAfter, StringComparer.Ordinal),
            "Process state facts do not match the expected committed boundary.");
        Require(state.BeforeFacts.OrderBy(atom => atom, StringComparer.Ordinal).SequenceEqual(["seed"], StringComparer.Ordinal),
            "Process state does not describe the original seed facts before the first committed round.");
        Require(state.ModelRevision == (state.Boundary == "accepted" ? 2 : 3)
                && state.CommittedRounds == (state.Boundary == "accepted" ? 0 : 1)
                && state.AcceptedModelRevision == 2
                && state.ConsumedModelRevision == (state.Boundary == "accepted" ? null : 3)
                && (state.Boundary == "accepted" ? state.PendingReceiptId == state.ReceiptId : state.PendingReceiptId is null),
            "Process state does not match the committed acceptance or consumption boundary.");
        state.Invocation = invocation;
        return state;
    }

    private static string ComputeReceiptId(RuleRoundReceipt receipt)
    {
        var roundId = RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(
            $"rules.round:1:{receipt.Identity.Fingerprint.Sha256}:{receipt.Identity.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        return RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(
            $"rules.receipt:1:{roundId}:{receipt.Identity.InputStateSha256}"));
    }

    private static bool TerminalsEqual(RuleTerminalResult? left, RuleTerminalResult? right) =>
        left is null ? right is null : right is not null
            && RuleRecordCodec.TerminalBytes(left).AsSpan().SequenceEqual(RuleRecordCodec.TerminalBytes(right));

    private sealed class ProcessBoundaryControl(string boundary, string statePath, string releasePath, string connection)
    {
        private int _capturing;
        private int _released;
        public string Boundary { get; } = boundary;
        public string StatePath { get; } = statePath;
        public string ReleasePath { get; } = releasePath;
        public string Connection { get; } = connection;
        public RuleInvocationEnvelope? Invocation { get; set; }
        public TaskCompletionSource<ProcessBoundaryState> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Resumed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryBeginCapture() => Interlocked.CompareExchange(ref _capturing, 1, 0) == 0;
        public void MarkReleased() => Volatile.Write(ref _released, 1);
        public bool HasReleased => Volatile.Read(ref _released) == 1;
    }

    private sealed class ProcessBoundaryStore(IRuleRunStore store, IRuleRunReader reader, ProcessBoundaryControl control)
        : IRuleRunStore, IRuleRunReader
    {
        public ValueTask<RuleAdmissionConfirmation> AdmitAsync(RuleRunAdmission admission, CancellationToken cancellationToken = default) =>
            store.AdmitAsync(admission, cancellationToken);
        public ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default) =>
            store.RefuseAdmissionAsync(refusal, cancellationToken);
        public ValueTask<RuleCancellationConfirmation> CancelAsync(RuleCancellationRequest request, CancellationToken cancellationToken = default) =>
            store.CancelAsync(request, cancellationToken);
        public ValueTask<RuleWorkClaimResult> ClaimWorkAsync(RuleClaimRequest request, CancellationToken cancellationToken = default) =>
            store.ClaimWorkAsync(request, cancellationToken);
        public ValueTask<RuleLeaseConfirmation> RenewAsync(RuleWorkLease lease, CancellationToken cancellationToken = default) =>
            store.RenewAsync(lease, cancellationToken);
        public async ValueTask<RuleLeaseConfirmation> ReleaseAsync(RuleWorkLease lease, CancellationToken cancellationToken = default)
        {
            var result = await store.ReleaseAsync(lease, cancellationToken);
            if (control.Boundary == "consumed" && control.HasReleased)
                control.Resumed.TrySetResult(result.Disposition.ToString());
            return result;
        }

        public async ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(RuleWorkToken token, RuleRoundSubmission submission,
            CancellationToken cancellationToken = default)
        {
            if (control.Boundary != "accepted" || !MatchesInvocation(token.Lease.Identity) || !control.TryBeginCapture())
                return await store.AcceptRoundAsync(token, submission, cancellationToken);
            try
            {
                var before = await reader.ReadWorkAsync(token, cancellationToken);
                Require(before.Disposition == RuleWorkReadDisposition.EvaluateRound && before.RoundInput is not null
                        && before.RoundInput.Identity.Ordinal == 0,
                    "Acceptance boundary did not observe the first actual evaluation input.");
                var result = await store.AcceptRoundAsync(token, submission, cancellationToken);
                Require(result.Disposition == RuleReceiptDisposition.Accepted && result.ReceiptId is not null
                        && result.ModelRevision is not null,
                    $"First round acceptance did not commit: {result.Disposition}.");
                var state = await CaptureCommittedStateAsync(before, submission.Receipt,
                    submission.Attempt, result.ReceiptId, result.ModelRevision.Value, consumedModelRevision: null,
                    cancellationToken);
                PublishState(state);
                await BlockForSuccessorAsync(cancellationToken);
                return result;
            }
            catch (Exception error)
            {
                control.Captured.TrySetException(error);
                throw;
            }
        }

        public async ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(RuleWorkToken token, RuleReceiptIdentity receipt,
            CancellationToken cancellationToken = default)
        {
            if (control.Boundary != "consumed" || !MatchesInvocation(token.Lease.Identity) || !control.TryBeginCapture())
                return await store.ConsumeRoundAsync(token, receipt, cancellationToken);
            try
            {
                var before = await reader.ReadWorkAsync(token, cancellationToken);
                Require(before.Disposition == RuleWorkReadDisposition.ConsumeReceipt && before.PendingReceipt is not null
                        && before.PendingReceipt.Receipt.Identity.Ordinal == 0
                        && before.PendingReceipt.ReceiptId == receipt.ReceiptId,
                    "Consumption boundary did not observe the first actual pending receipt.");
                var pending = before.PendingReceipt;
                var result = await store.ConsumeRoundAsync(token, receipt, cancellationToken);
                Require(result.Disposition == RuleTransitionDisposition.Consumed && result.ModelRevision is not null,
                    $"First round consumption did not commit: {result.Disposition}.");
                var state = await CaptureCommittedStateAsync(before, pending.Receipt, pending.AcceptedAttempt,
                    pending.ReceiptId, pending.AcceptedModelRevision, result.ModelRevision.Value, cancellationToken);
                PublishState(state);
                await BlockForSuccessorAsync(cancellationToken);
                return result;
            }
            catch (Exception error)
            {
                control.Captured.TrySetException(error);
                throw;
            }
        }

        private bool MatchesInvocation(RuleRunIdentity identity) =>
            control.Invocation is { } invocation && invocation.Identity == identity;

        private async Task<ProcessBoundaryState> CaptureCommittedStateAsync(RuleWorkReadResult before,
            RuleRoundReceipt receipt, RuleRoundAttempt attempt, string? receiptId, long acceptedModelRevision,
            long? consumedModelRevision, CancellationToken cancellationToken)
        {
            var invocation = control.Invocation ?? throw new ProbeAssertionException("Boundary invocation was not installed.");
            Require(receiptId is not null && receipt.Identity.Ordinal == 0,
                "The committed process receipt is not ordinal zero.");
            var current = await reader.FindAsync(invocation.Identity, cancellationToken);
            Require(current.Disposition == RuleRunReadDisposition.Found && current.View is not null,
                "Committed process boundary could not be read back.");
            var page = await ReadProgressPageAsync(reader, invocation.Identity, cancellationToken);
            var facts = page.Items.Where(item => item.Kind == RuleProgressItemKind.Fact).Select(item => item.Fact!.Atom)
                .OrderBy(atom => atom, StringComparer.Ordinal).ToArray();
            var round = page.Items.Where(item => item.Kind == RuleProgressItemKind.Round)
                .Select(item => item.Round!).SingleOrDefault(item => item.Ordinal == 0)
                ?? throw new ProbeAssertionException("Committed process boundary did not persist round zero.");
            Require(round.ReceiptId == receiptId && round.SemanticReceiptSha256 == receipt.SemanticReceiptSha256
                    && round.AcceptedAttemptId == attempt.AttemptId && round.AcceptedWorkerId == attempt.WorkerId
                    && round.AcceptedLeaseFence == attempt.LeaseFence
                    && round.AcceptedModelRevision == acceptedModelRevision
                    && round.ConsumedModelRevision == consumedModelRevision,
                "Committed process boundary read-back differs from the actual receipt and attempt.");
            var expectedFacts = control.Boundary == "accepted" ? new[] { "seed" } : new[] { "left", "right", "seed" };
            var expectedRounds = control.Boundary == "accepted" ? 0 : 1;
            var expectedRevision = control.Boundary == "accepted" ? 2 : 3;
            Require(current.View.Status == RuleRunStatus.Running && current.View.ModelRevision == expectedRevision
                    && current.View.CommittedRounds == expectedRounds
                    && current.View.PendingReceiptId == (control.Boundary == "accepted" ? receiptId : null)
                    && facts.SequenceEqual(expectedFacts, StringComparer.Ordinal),
                "Committed process boundary differs from its expected durable state.");
            var receiptBytes = RuleRecordCodec.ReceiptRecordBytes(receipt);
            var invocationBytes = RuleRecordCodec.InvocationBytes(invocation);
            var profile = await ReadPostgresProfileAsync(control.Connection, cancellationToken);
            var workToken = before.CurrentToken ?? throw new ProbeAssertionException("Boundary work token is missing.");
            return new ProcessBoundaryState
            {
                SchemaVersion = 1,
                Boundary = control.Boundary,
                InvocationBase64 = Convert.ToBase64String(invocationBytes),
                InvocationFingerprint = RuleRecordCodec.Fingerprint(invocation).Sha256,
                ReceiptRecordBase64 = Convert.ToBase64String(receiptBytes),
                ReceiptId = receiptId,
                SemanticReceiptSha256 = receipt.SemanticReceiptSha256,
                InputStateSha256 = receipt.Identity.InputStateSha256,
                BeforeFacts = (before.RoundInput?.Facts ?? invocation.InitialFacts).Select(fact => fact.Atom)
                    .OrderBy(atom => atom, StringComparer.Ordinal).ToArray(),
                AfterFacts = facts,
                ModelRevision = current.View.ModelRevision,
                CommittedRounds = current.View.CommittedRounds,
                PendingReceiptId = current.View.PendingReceiptId,
                AcceptedAttemptId = attempt.AttemptId,
                AcceptedWorkerId = attempt.WorkerId,
                AcceptedLeaseFence = attempt.LeaseFence,
                AcceptedModelRevision = acceptedModelRevision,
                ConsumedModelRevision = consumedModelRevision,
                TokenOwner = workToken.Lease.Owner,
                TokenFence = workToken.Lease.Fence,
                TokenExpiresAtUtc = workToken.Lease.ExpiresAt,
                TokenExpectedModelRevision = workToken.ExpectedModelRevision,
                DatabaseEngine = profile.Engine,
                DatabaseEngineVersion = profile.Version,
                DatabaseName = profile.DatabaseName,
                ProcessId = Environment.ProcessId,
                MachineName = Environment.MachineName,
                CapturedUtc = DateTimeOffset.UtcNow
            };
        }

        private void PublishState(ProcessBoundaryState state)
        {
            WriteJson(control.StatePath, state);
            control.Captured.TrySetResult(state);
        }

        private async Task BlockForSuccessorAsync(CancellationToken cancellationToken)
        {
            await WaitForReleaseAsync(control.ReleasePath, cancellationToken);
            control.MarkReleased();
        }

        public async ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default)
        {
            var result = await reader.ReadWorkAsync(token, cancellationToken);
            if (control.Boundary == "accepted" && control.HasReleased)
                control.Resumed.TrySetResult(result.Disposition.ToString());
            return result;
        }
        public ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(RuleDeliveryLease lease, CancellationToken cancellationToken = default) => reader.ReadDeliveryAsync(lease, cancellationToken);
        public ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) => reader.FindAsync(identity, cancellationToken);
        public ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default) => reader.ReadTerminalAsync(identity, cancellationToken);
        public ValueTask<RuleProgressReadResult> ReadProgressAsync(RuleRunIdentity identity, RuleProgressPosition? after, int take, CancellationToken cancellationToken = default) => reader.ReadProgressAsync(identity, after, take, cancellationToken);

        public ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(RuleDeliveryClaimRequest request, CancellationToken cancellationToken = default) => store.ClaimDeliveryAsync(request, cancellationToken);
        public ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(RuleDeliveryLease lease, RuleDeliveryAcknowledgement acknowledgement, CancellationToken cancellationToken = default) => store.AcknowledgeDeliveryAsync(lease, acknowledgement, cancellationToken);
    }

    private sealed class ProcessBoundaryState
    {
        public ProcessBoundaryState() { }

        public int SchemaVersion { get; init; }
        public string Boundary { get; init; } = string.Empty;
        public string InvocationBase64 { get; init; } = string.Empty;
        public string InvocationFingerprint { get; init; } = string.Empty;
        public string ReceiptRecordBase64 { get; init; } = string.Empty;
        public string ReceiptId { get; init; } = string.Empty;
        public string SemanticReceiptSha256 { get; init; } = string.Empty;
        public string InputStateSha256 { get; init; } = string.Empty;
        public string[] BeforeFacts { get; init; } = [];
        public string[] AfterFacts { get; init; } = [];
        public long ModelRevision { get; init; }
        public int CommittedRounds { get; init; }
        public string? PendingReceiptId { get; init; }
        public string AcceptedAttemptId { get; init; } = string.Empty;
        public string AcceptedWorkerId { get; init; } = string.Empty;
        public long AcceptedLeaseFence { get; init; }
        public long AcceptedModelRevision { get; init; }
        public long? ConsumedModelRevision { get; init; }
        public string TokenOwner { get; init; } = string.Empty;
        public long TokenFence { get; init; }
        public DateTimeOffset TokenExpiresAtUtc { get; init; }
        public long TokenExpectedModelRevision { get; init; }
        public string DatabaseEngine { get; init; } = string.Empty;
        public string DatabaseEngineVersion { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public int ProcessId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public DateTimeOffset CapturedUtc { get; init; }
        [JsonIgnore] public RuleInvocationEnvelope Invocation { get; set; } = null!;
    }

    private sealed record PostgresProfile(string Engine, string Version, string DatabaseName);
    private sealed record RunSnapshot(RuleRunView View, RuleTerminalResult? Terminal, string[] Facts,
        RuleRoundProgress[] Rounds, string[] TerminalFacts);
    private sealed record StableFinal(RuleRunView View, RuleTerminalResult Terminal, string[] Facts);
}
