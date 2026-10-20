using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;

internal static class AdmissionProbe
{
    private const string ClusterId = "membership-probe";
    private static readonly TimeSpan LivenessExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(2);

    public static async Task<AdmissionSuiteReceipt> RunAsync(string connectionString)
    {
        await InitializeAdmissionSchema(connectionString);

        var bootstrapIncumbent = NewIdentity("127.0.0.11", 11111, 3101);
        var bootstrapCandidate = NewIdentity("127.0.0.12", 11112, 3102);
        var heartbeatIncumbent = NewIdentity("127.0.0.21", 11211, 3201);
        var heartbeatCandidate = NewIdentity("127.0.0.22", 11212, 3202);
        var statusIncumbent = NewIdentity("127.0.0.31", 11311, 3301);
        var statusCandidate = NewIdentity("127.0.0.32", 11312, 3302);

        using (var seeder = BuildMembershipHost(connectionString, "admission-red-seeder"))
        {
            var membership = await InitializeMembershipTable(seeder);
            await InsertMembership(membership, bootstrapIncumbent, "probe-bootstrap-incumbent", DateTime.UtcNow);
            await InsertMembership(membership, heartbeatIncumbent, "probe-heartbeat-incumbent", DateTime.UtcNow);
            await InsertMembership(membership, statusIncumbent, "probe-status-incumbent", DateTime.UtcNow);
        }

        var bootstrap = await RunInitialAdmissionRace(connectionString, bootstrapIncumbent, bootstrapCandidate);
        var heartbeat = await RunDelayedWriteRace(
            connectionString,
            "heartbeat-host",
            heartbeatIncumbent,
            heartbeatCandidate,
            isHeartbeat: true);
        var status = await RunDelayedWriteRace(
            connectionString,
            "status-host",
            statusIncumbent,
            statusCandidate,
            isHeartbeat: false);

        return new AdmissionSuiteReceipt
        {
            ProviderTopology = "Orleans 10.4.0 ADO.NET/Npgsql PostgreSQL 16; public IMembershipTable resolved through supported DI and explicitly initialized; no IHost.StartAsync, silo, or Orleans membership loops",
            HostIdentityPolicy = "Fixture host IDs are explicit stable labels and are not inferred from SiloName or HostName",
            LivenessExpirySeconds = (int)LivenessExpiry.TotalSeconds,
            AllowedClockSkewSeconds = (int)AllowedClockSkew.TotalSeconds,
            AdmissionUsesOrleansStartAndHeartbeat = true,
            SeparateProbeHeartbeatAuthority = false,
            InitialAdmissionRace = bootstrap,
            Heartbeat = heartbeat,
            Status = status,
            DesiredHeartbeatRefused = !heartbeat.UnsafeCommitObserved,
            DesiredStatusRefused = !status.UnsafeCommitObserved
        };
    }

    private static async Task InitializeAdmissionSchema(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE TABLE probe_host_admission (
                host_id text PRIMARY KEY,
                current_address inet NULL,
                current_port integer NULL,
                current_generation integer NULL,
                current_state text NOT NULL CHECK (current_state IN ('unmapped', 'pending', 'admitted')),
                CHECK (
                    (current_state = 'unmapped' AND current_address IS NULL AND current_port IS NULL AND current_generation IS NULL)
                    OR
                    (current_state <> 'unmapped' AND current_address IS NOT NULL AND current_port IS NOT NULL AND current_generation IS NOT NULL)
                )
            );
            CREATE TABLE probe_host_incarnation (
                cluster_id text NOT NULL,
                address inet NOT NULL,
                port integer NOT NULL,
                generation integer NOT NULL,
                host_id text NOT NULL REFERENCES probe_host_admission(host_id),
                PRIMARY KEY (cluster_id, address, port, generation)
            );
            CREATE FUNCTION probe_reject_incarnation_rebind() RETURNS trigger AS $func$
            BEGIN
                IF ROW(NEW.cluster_id, NEW.address, NEW.port, NEW.generation, NEW.host_id)
                    IS DISTINCT FROM ROW(OLD.cluster_id, OLD.address, OLD.port, OLD.generation, OLD.host_id) THEN
                    RAISE EXCEPTION 'probe incarnation-to-host binding is immutable';
                END IF;
                RETURN NEW;
            END;
            $func$ LANGUAGE plpgsql;
            CREATE TRIGGER probe_host_incarnation_immutable
                BEFORE UPDATE ON probe_host_incarnation
                FOR EACH ROW EXECUTE FUNCTION probe_reject_incarnation_rebind();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<InitialAdmissionReceipt> RunInitialAdmissionRace(
        string connectionString,
        SiloIdentity incumbent,
        SiloIdentity candidate)
    {
        const string hostId = "initial-admission-host";
        var incumbentApp = "admission-red-initial-winner";
        var contenderApp = "admission-red-initial-contender";
        var incumbentConnection = CreateConnection(connectionString, incumbentApp);
        NpgsqlTransaction? incumbentTransaction = null;
        try
        {
            await incumbentConnection.OpenAsync();
            incumbentTransaction = await incumbentConnection.BeginTransactionAsync();
            var incumbentPid = await ReadBackendPid(incumbentConnection, incumbentTransaction);
            await InsertUnmappedHost(incumbentConnection, incumbentTransaction, hostId);
            var locked = await ReadHostAdmission(incumbentConnection, incumbentTransaction, hostId, forUpdate: true);
            Require(locked.State == "unmapped", "initial-host-row-unmapped", "first candidate did not lock the new host row");
            await InsertIncarnationBinding(incumbentConnection, incumbentTransaction, hostId, incumbent);
            var firstLiveness = await ReadLiveness(incumbentConnection, incumbentTransaction, incumbent);
            Require(IsFresh(firstLiveness), "initial-incumbent-fresh", "bootstrap fixture incumbent is not fresh under the stated policy");
            await SetCurrentAdmission(incumbentConnection, incumbentTransaction, hostId, incumbent, "admitted");

            var contender = TryInitialContender(connectionString, hostId, candidate, incumbent, contenderApp);
            var blocked = await WaitForBlockedBackend(connectionString, contenderApp, incumbentPid, contender);
            await incumbentTransaction.CommitAsync();
            var contenderResult = await contender.WaitAsync(TimeSpan.FromSeconds(15));
            var final = await ReadHostAdmission(connectionString, hostId);
            return new InitialAdmissionReceipt
            {
                FirstBackendPid = incumbentPid,
                ContenderBackendPid = blocked.Pid,
                ContenderWaitEventType = blocked.WaitEventType,
                ContenderWaitEvent = blocked.WaitEvent,
                BlockingBackendPid = incumbentPid,
                FirstCandidateConfirmed = true,
                ContenderObservedFreshIncumbent = contenderResult.ObservedFreshIncumbent,
                ContenderIncumbentStartTimeUtc = contenderResult.Liveness.StartTime,
                ContenderIncumbentIAmAliveTimeUtc = contenderResult.Liveness.IAmAliveTime,
                ContenderLivenessDatabaseTimeUtc = contenderResult.Liveness.DatabaseTime,
                ContenderWasRefused = contenderResult.Refused,
                ContenderBindingRecordedAsNonCurrent = contenderResult.BindingRecorded,
                CurrentIdentity = final.Identity?.ToString(),
                CurrentState = final.State,
                MissingHostRowSerialized = contenderResult.Refused && final.Identity == incumbent && final.State == "admitted"
            };
        }
        finally
        {
            if (incumbentTransaction is not null)
            {
                try { await incumbentTransaction.DisposeAsync(); }
                catch (NpgsqlException) { }
            }
            try { await incumbentConnection.DisposeAsync(); }
            catch (NpgsqlException) { }
        }
    }

    private static async Task<InitialContenderResult> TryInitialContender(
        string connectionString,
        string hostId,
        SiloIdentity candidate,
        SiloIdentity incumbent,
        string applicationName)
    {
        await using var connection = CreateConnection(connectionString, applicationName);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertUnmappedHost(connection, transaction, hostId);
        var locked = await ReadHostAdmission(connection, transaction, hostId, forUpdate: true);
        await InsertIncarnationBinding(connection, transaction, hostId, candidate);
        var incumbentLiveness = await ReadLiveness(connection, transaction, incumbent);
        var observedFresh = locked.Identity == incumbent && locked.State == "admitted" && IsFresh(incumbentLiveness);
        await transaction.CommitAsync();
        return new InitialContenderResult(observedFresh, observedFresh, true, incumbentLiveness);
    }

    private static async Task<DelayedWriteReceipt> RunDelayedWriteRace(
        string connectionString,
        string hostId,
        SiloIdentity incumbent,
        SiloIdentity successor,
        bool isHeartbeat)
    {
        await EnsureAdmittedMapping(connectionString, hostId, incumbent);
        await SetMembershipStale(connectionString, incumbent);

        var caseName = isHeartbeat ? "heartbeat" : "status";
        var callerApp = $"admission-red-{caseName}-writer";
        var barrierApp = $"admission-red-{caseName}-barrier";
        var admissionApp = $"admission-red-{caseName}-admission";
        using var callerHost = BuildMembershipHost(connectionString, callerApp);
        var membership = await InitializeMembershipTable(callerHost);
        var membershipData = await membership.ReadRowAsync(incumbent.ToSiloAddress());
        Require(membershipData.Members.Count == 1, $"{caseName}-incumbent-read", "provider did not return exactly one incumbent row");
        var (entry, etag) = membershipData.Members.Single();
        var capturedVersion = membershipData.Version.Version;
        var capturedEtag = membershipData.Version.VersionEtag;
        Require(etag == capturedEtag, $"{caseName}-valid-provider-token", "provider row etag and table token do not match");

        var before = await ReadMembershipState(connectionString, incumbent);
        var versionBefore = await ReadMembershipTableVersion(connectionString);
        await using var barrierConnection = CreateConnection(connectionString, barrierApp);
        await barrierConnection.OpenAsync();
        await using var barrierTransaction = await barrierConnection.BeginTransactionAsync();
        var barrierPid = await ReadBackendPid(barrierConnection, barrierTransaction);
        await LockMembershipRow(barrierConnection, barrierTransaction, incumbent);

        await using var admissionConnection = CreateConnection(connectionString, admissionApp);
        await admissionConnection.OpenAsync();
        var admissionTransaction = await admissionConnection.BeginTransactionAsync();
        var admissionPid = await ReadBackendPid(admissionConnection, admissionTransaction);
        var mappingAtCheck = await ReadHostAdmission(admissionConnection, admissionTransaction, hostId, forUpdate: true);
        Require(mappingAtCheck.Identity == incumbent && mappingAtCheck.State == "admitted",
            $"{caseName}-old-mapping-locked", $"expected {incumbent} admitted, got {mappingAtCheck.Identity} {mappingAtCheck.State}");
        var incumbentLiveness = await ReadLiveness(admissionConnection, admissionTransaction, incumbent);
        Require(!IsFresh(incumbentLiveness), $"{caseName}-incumbent-expired", "fixture incumbent unexpectedly remains fresh");

        Task<bool>? statusWrite = null;
        Task? heartbeatWrite = null;
        if (isHeartbeat)
        {
            entry.IAmAliveTime = DateTime.UtcNow;
            heartbeatWrite = membership.UpdateIAmAliveAsync(entry);
        }
        else
        {
            entry.Status = SiloStatus.Active;
            statusWrite = membership.UpdateRowAsync(entry, etag, membershipData.Version);
        }

        Task providerOperation = isHeartbeat ? heartbeatWrite! : statusWrite!;
        var blocked = await WaitForBlockedBackend(connectionString, callerApp, barrierPid, providerOperation);
        var admissionKilled = await TerminateBackend(connectionString, admissionPid);
        Require(admissionKilled, $"{caseName}-admission-session-killed", "PostgreSQL did not confirm termination of the admission transaction");
        await WaitForBackendGone(connectionString, admissionPid);
        try { await admissionTransaction.DisposeAsync(); }
        catch (NpgsqlException) { }
        await admissionConnection.CloseAsync();

        var remapLiveness = await RegisterPendingSuccessorAndRemap(connectionString, hostId, incumbent, successor);
        var mappingAfterRemap = await ReadHostAdmission(connectionString, hostId);
        Require(mappingAfterRemap.Identity == successor && mappingAfterRemap.State == "pending",
            $"{caseName}-successor-remapped-pending", "successor mapping was not committed in pending state");
        var versionAfterRemap = await ReadMembershipTableVersion(connectionString);
        Require(versionAfterRemap.Version == versionBefore.Version && versionAfterRemap.Timestamp == versionBefore.Timestamp,
            $"{caseName}-remap-membership-version-neutral", "probe-owned remap changed Orleans membership table version");

        await barrierTransaction.CommitAsync();
        var providerCallCompleted = isHeartbeat
            ? await CompleteHeartbeat(heartbeatWrite!)
            : await CompleteStatus(statusWrite!);
        var after = await ReadMembershipState(connectionString, incumbent);
        var versionAfter = await ReadMembershipTableVersion(connectionString);
        var finalMapping = await ReadHostAdmission(connectionString, hostId);
        var unsafeCommit = isHeartbeat
            ? providerCallCompleted && after.IAmAliveTime > before.IAmAliveTime && finalMapping.Identity == successor
            : providerCallCompleted && after.Status == (int)SiloStatus.Active && finalMapping.Identity == successor;

        return new DelayedWriteReceipt
        {
            Operation = isHeartbeat ? "UpdateIAmAliveAsync" : "UpdateRowAsync",
            IncumbentIdentity = incumbent.ToString(),
            SuccessorIdentity = successor.ToString(),
            SuccessorStateAtOldCommit = finalMapping.State,
            AdmissionBackendPid = admissionPid,
            AdmissionBackendTerminated = admissionKilled,
            BarrierBackendPid = barrierPid,
            ProviderBackendPid = blocked.Pid,
            ProviderWaitEventType = blocked.WaitEventType,
            ProviderWaitEvent = blocked.WaitEvent,
            ProviderWasBlockedByBarrier = blocked.BlockerPid == barrierPid,
            SuccessorRemapCommittedBeforeBarrierRelease = true,
            IncumbentStartTimeUtc = remapLiveness.StartTime,
            IncumbentIAmAliveTimeUtc = remapLiveness.IAmAliveTime,
            RemapLivenessDatabaseTimeUtc = remapLiveness.DatabaseTime,
            IncumbentWasExpiredAtRemap = !remapLiveness.IsFresh,
            CapturedTableVersion = capturedVersion,
            CapturedTableVersionEtag = capturedEtag,
            MembershipVersionBefore = versionBefore.Version,
            MembershipVersionAfterRemapBeforeProviderCommit = versionAfterRemap.Version,
            MembershipVersionAfterProviderCommit = versionAfter.Version,
            ProviderCallCompleted = providerCallCompleted,
            BeforeStatus = before.Status,
            AfterStatus = after.Status,
            BeforeIAmAliveTimeUtc = before.IAmAliveTime,
            AfterIAmAliveTimeUtc = after.IAmAliveTime,
            UnsafeCommitObserved = unsafeCommit,
            DesiredStaleWriteRefused = !unsafeCommit,
            HeartbeatLeftMembershipTableVersionUnchanged = isHeartbeat && versionAfter.Version == versionBefore.Version,
            StatusTokenRemainedCurrentThroughRemap = !isHeartbeat && versionAfterRemap.Version == capturedVersion && versionAfterRemap.Timestamp == versionBefore.Timestamp
        };
    }

    private static async Task<bool> CompleteStatus(Task<bool> task) => await task.WaitAsync(TimeSpan.FromSeconds(15));

    private static async Task<bool> CompleteHeartbeat(Task task)
    {
        await task.WaitAsync(TimeSpan.FromSeconds(15));
        return true;
    }

    private static async Task EnsureAdmittedMapping(string connectionString, string hostId, SiloIdentity incumbent)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertUnmappedHost(connection, transaction, hostId);
        _ = await ReadHostAdmission(connection, transaction, hostId, forUpdate: true);
        await InsertIncarnationBinding(connection, transaction, hostId, incumbent);
        await SetCurrentAdmission(connection, transaction, hostId, incumbent, "admitted");
        await transaction.CommitAsync();
    }

    private static async Task<LivenessObservation> RegisterPendingSuccessorAndRemap(
        string connectionString,
        string hostId,
        SiloIdentity expectedIncumbent,
        SiloIdentity successor)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var mapping = await ReadHostAdmission(connection, transaction, hostId, forUpdate: true);
        Require(mapping.Identity == expectedIncumbent && mapping.State == "admitted",
            "successor-remap-incumbent", "candidate remap did not lock the expected admitted incumbent");
        await InsertIncarnationBinding(connection, transaction, hostId, successor);
        var liveness = await ReadLiveness(connection, transaction, expectedIncumbent);
        Require(!IsFresh(liveness), "successor-remap-rechecks-expiry", "candidate remap did not re-confirm incumbent expiry under the host lock");
        await using var command = new NpgsqlCommand("""
            UPDATE probe_host_admission
            SET current_address = @successor_address::inet,
                current_port = @successor_port,
                current_generation = @successor_generation,
                current_state = 'pending'
            WHERE host_id = @host_id
              AND current_address = @incumbent_address::inet
              AND current_port = @incumbent_port
              AND current_generation = @incumbent_generation
              AND current_state = 'admitted';
            """, connection, transaction);
        AddIdentityParameters(command, "successor", successor);
        AddIdentityParameters(command, "incumbent", expectedIncumbent);
        command.Parameters.AddWithValue("host_id", hostId);
        Require(await command.ExecuteNonQueryAsync() == 1, "successor-remap-cas", "successor remap did not match the expected incumbent mapping");
        await transaction.CommitAsync();
        return liveness;
    }

    private static async Task<bool> InsertMembership(IMembershipTable membership, SiloIdentity identity, string siloName, DateTime iAmAliveTime)
    {
        var current = await membership.ReadAllAsync();
        var entry = new MembershipEntry
        {
            SiloAddress = identity.ToSiloAddress(),
            SiloName = siloName,
            HostName = "probe-host",
            Status = SiloStatus.Joining,
            ProxyPort = identity.Port + 1,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            IAmAliveTime = iAmAliveTime
        };
        Require(await membership.InsertRowAsync(entry, current.Version), "membership-seed-insert", "provider refused a unique membership fixture row");
        return true;
    }

    private static IHost BuildMembershipHost(string connectionString, string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = applicationName };
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleans(orleans => orleans
                .Configure<SiloOptions>(options => options.SiloName = applicationName)
                .Configure<ClusterOptions>(options => { options.ClusterId = ClusterId; options.ServiceId = ClusterId; })
                .UseAdoNetClustering(options => { options.Invariant = "Npgsql"; options.ConnectionString = builder.ConnectionString; }))
            .Build();
    }

    private static async Task<IMembershipTable> InitializeMembershipTable(IHost host)
    {
        var membership = host.Services.GetRequiredService<IMembershipTable>();
        await membership.InitializeMembershipTableAsync(true);
        return membership;
    }

    private static async Task SetMembershipStale(string connectionString, SiloIdentity identity)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE OrleansMembershipTable
            SET IAmAliveTime = clock_timestamp() - interval '2 minutes'
            WHERE DeploymentId = @deployment_id AND Address = @incumbent_address AND Port = @incumbent_port AND Generation = @incumbent_generation;
            """, connection);
        AddIdentityParameters(command, "incumbent", identity);
        command.Parameters.AddWithValue("deployment_id", ClusterId);
        Require(await command.ExecuteNonQueryAsync() == 1, "membership-make-stale", "could not age the incumbent heartbeat fixture");
    }

    private static async Task LockMembershipRow(NpgsqlConnection connection, NpgsqlTransaction transaction, SiloIdentity identity)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1 FROM OrleansMembershipTable
            WHERE DeploymentId = @deployment_id AND Address = @incumbent_address AND Port = @incumbent_port AND Generation = @incumbent_generation
            FOR UPDATE;
            """, connection, transaction);
        AddIdentityParameters(command, "incumbent", identity);
        command.Parameters.AddWithValue("deployment_id", ClusterId);
        Require(await command.ExecuteScalarAsync() is not null, "membership-barrier-row", "membership row barrier did not lock exactly one row");
    }

    private static async Task<MembershipState> ReadMembershipState(string connectionString, SiloIdentity identity)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await ReadMembershipState(connection, null, identity);
    }

    private static async Task<MembershipState> ReadMembershipState(NpgsqlConnection connection, NpgsqlTransaction? transaction, SiloIdentity identity)
    {
        await using var command = new NpgsqlCommand("""
            SELECT StartTime, IAmAliveTime, Status
            FROM OrleansMembershipTable
            WHERE DeploymentId = @deployment_id AND Address = @incumbent_address AND Port = @incumbent_port AND Generation = @incumbent_generation;
            """, connection, transaction);
        AddIdentityParameters(command, "incumbent", identity);
        command.Parameters.AddWithValue("deployment_id", ClusterId);
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "membership-fixture-row", "expected membership row is missing");
        return new MembershipState(reader.GetDateTime(0), reader.GetDateTime(1), reader.GetInt32(2));
    }

    private static async Task<LivenessObservation> ReadLiveness(NpgsqlConnection connection, NpgsqlTransaction? transaction, SiloIdentity identity)
    {
        var state = await ReadMembershipState(connection, transaction, identity);
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection, transaction);
        var now = (DateTime)(await command.ExecuteScalarAsync())!;
        var fresh = state.StartTime <= now + AllowedClockSkew
            && state.IAmAliveTime >= now - LivenessExpiry - AllowedClockSkew
            && state.IAmAliveTime >= state.StartTime - AllowedClockSkew;
        return new LivenessObservation(state.StartTime, state.IAmAliveTime, now, fresh);
    }

    private static bool IsFresh(LivenessObservation observation) => observation.IsFresh;

    private static async Task<HostAdmission> ReadHostAdmission(string connectionString, string hostId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await ReadHostAdmission(connection, null, hostId, forUpdate: false);
    }

    private static async Task<HostAdmission> ReadHostAdmission(NpgsqlConnection connection, NpgsqlTransaction? transaction, string hostId, bool forUpdate)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT host(current_address), current_port, current_generation, current_state
            FROM probe_host_admission
            WHERE host_id = @host_id{(forUpdate ? " FOR UPDATE" : string.Empty)};
            """, connection, transaction);
        command.Parameters.AddWithValue("host_id", hostId);
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "admission-host-row", "probe-owned host admission row is missing");
        var identity = reader.IsDBNull(0)
            ? (SiloIdentity?)null
            : new SiloIdentity(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
        return new HostAdmission(identity, reader.GetString(3));
    }

    private static async Task InsertUnmappedHost(NpgsqlConnection connection, NpgsqlTransaction transaction, string hostId)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO probe_host_admission(host_id, current_state)
            VALUES (@host_id, 'unmapped')
            ON CONFLICT (host_id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("host_id", hostId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertIncarnationBinding(NpgsqlConnection connection, NpgsqlTransaction transaction, string hostId, SiloIdentity identity)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO probe_host_incarnation(cluster_id, address, port, generation, host_id)
            VALUES (@cluster_id, @incarnation_address::inet, @incarnation_port, @incarnation_generation, @host_id)
            ON CONFLICT (cluster_id, address, port, generation) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("cluster_id", ClusterId);
        AddIdentityParameters(command, "incarnation", identity);
        command.Parameters.AddWithValue("host_id", hostId);
        await command.ExecuteNonQueryAsync();
        await using var verify = new NpgsqlCommand("""
            SELECT host_id FROM probe_host_incarnation
            WHERE cluster_id = @cluster_id AND address = @incarnation_address::inet AND port = @incarnation_port AND generation = @incarnation_generation;
            """, connection, transaction);
        verify.Parameters.AddWithValue("cluster_id", ClusterId);
        AddIdentityParameters(verify, "incarnation", identity);
        Require((string?)await verify.ExecuteScalarAsync() == hostId, "incarnation-binding-immutable", "an incarnation is already bound to another fixture host");
    }

    private static async Task SetCurrentAdmission(NpgsqlConnection connection, NpgsqlTransaction transaction, string hostId, SiloIdentity identity, string state)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE probe_host_admission
            SET current_address = @incarnation_address::inet, current_port = @incarnation_port, current_generation = @incarnation_generation,
                current_state = @state
            WHERE host_id = @host_id;
            """, connection, transaction);
        AddIdentityParameters(command, "incarnation", identity);
        command.Parameters.AddWithValue("host_id", hostId);
        command.Parameters.AddWithValue("state", state);
        Require(await command.ExecuteNonQueryAsync() == 1, "admission-host-update", "probe-owned host mapping row was not updated");
    }

    private static async Task<MembershipVersionState> ReadMembershipTableVersion(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT Version, Timestamp FROM OrleansMembershipVersionTable WHERE DeploymentId = @deployment_id;
            """, connection);
        command.Parameters.AddWithValue("deployment_id", ClusterId);
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "membership-version-row", "Orleans membership version row is missing");
        return new MembershipVersionState(reader.GetInt32(0), reader.GetDateTime(1));
    }

    private static async Task<int> ReadBackendPid(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection, transaction);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TerminateBackend(string connectionString, int pid)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", pid);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitForBackendGone(string connectionString, int pid)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var command = new NpgsqlCommand("SELECT NOT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid)", connection);
            command.Parameters.AddWithValue("pid", pid);
            if ((bool)(await command.ExecuteScalarAsync())!) return;
            await Task.Delay(50);
        }
        throw new ProbeFailure("admission-backend-termination-timeout", "PostgreSQL backend did not disappear after termination");
    }

    private static async Task<BlockedBackend> WaitForBlockedBackend(string connectionString, string applicationName, int blockerPid, Task operation)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (operation.IsCompleted)
                throw new ProbeFailure("provider-write-not-blocked", "actual provider write completed before the deterministic row barrier was released");
            await using var command = new NpgsqlCommand("""
                SELECT pid, wait_event_type, wait_event, pg_blocking_pids(pid)
                FROM pg_stat_activity
                WHERE application_name = @application_name AND state = 'active'
                ORDER BY query_start DESC
                LIMIT 1;
                """, connection);
            command.Parameters.AddWithValue("application_name", applicationName);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var pid = reader.GetInt32(0);
                var waitType = reader.IsDBNull(1) ? null : reader.GetString(1);
                var waitEvent = reader.IsDBNull(2) ? null : reader.GetString(2);
                var blockers = reader.GetFieldValue<int[]>(3);
                if (waitType == "Lock" && blockers.Contains(blockerPid))
                    return new BlockedBackend(pid, waitType, waitEvent, blockerPid);
            }
            await reader.CloseAsync();
            await Task.Delay(50);
        }
        throw new ProbeFailure("provider-lock-not-observed", "pg_stat_activity did not show the provider backend blocked by the row barrier");
    }

    private static NpgsqlConnection CreateConnection(string connectionString, string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = applicationName };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    private static void AddIdentityParameters(NpgsqlCommand command, string prefix, SiloIdentity identity)
    {
        command.Parameters.AddWithValue($"{prefix}_address", NpgsqlDbType.Text, identity.Address);
        command.Parameters.AddWithValue($"{prefix}_port", identity.Port);
        command.Parameters.AddWithValue($"{prefix}_generation", identity.Generation);
    }

    private static SiloIdentity NewIdentity(string address, int port, int generation) => new(address, port, generation);

    private static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new ProbeFailure(code, message);
    }

    private sealed record InitialContenderResult(bool ObservedFreshIncumbent, bool Refused, bool BindingRecorded, LivenessObservation Liveness);
    private sealed record BlockedBackend(int Pid, string? WaitEventType, string? WaitEvent, int BlockerPid);
    private sealed record HostAdmission(SiloIdentity? Identity, string State);
    private sealed record LivenessObservation(DateTime StartTime, DateTime IAmAliveTime, DateTime DatabaseTime, bool IsFresh);
    private sealed record MembershipState(DateTime StartTime, DateTime IAmAliveTime, int Status);
    private sealed record MembershipVersionState(int Version, DateTime Timestamp);

    private sealed record SiloIdentity(string Address, int Port, int Generation)
    {
        public SiloAddress ToSiloAddress() => SiloAddress.New(IPAddress.Parse(Address), Port, Generation);
        public override string ToString() => $"{Address}:{Port}@{Generation}";
    }
}

internal sealed class AdmissionSuiteReceipt
{
    public string ProviderTopology { get; set; } = "";
    public string HostIdentityPolicy { get; set; } = "";
    public int LivenessExpirySeconds { get; set; }
    public int AllowedClockSkewSeconds { get; set; }
    public bool AdmissionUsesOrleansStartAndHeartbeat { get; set; }
    public bool SeparateProbeHeartbeatAuthority { get; set; }
    public InitialAdmissionReceipt InitialAdmissionRace { get; set; } = new();
    public DelayedWriteReceipt Heartbeat { get; set; } = new();
    public DelayedWriteReceipt Status { get; set; } = new();
    public bool DesiredHeartbeatRefused { get; set; }
    public bool DesiredStatusRefused { get; set; }
}

internal sealed class InitialAdmissionReceipt
{
    public int FirstBackendPid { get; set; }
    public int ContenderBackendPid { get; set; }
    public string? ContenderWaitEventType { get; set; }
    public string? ContenderWaitEvent { get; set; }
    public int BlockingBackendPid { get; set; }
    public bool FirstCandidateConfirmed { get; set; }
    public bool ContenderObservedFreshIncumbent { get; set; }
    public DateTime ContenderIncumbentStartTimeUtc { get; set; }
    public DateTime ContenderIncumbentIAmAliveTimeUtc { get; set; }
    public DateTime ContenderLivenessDatabaseTimeUtc { get; set; }
    public bool ContenderWasRefused { get; set; }
    public bool ContenderBindingRecordedAsNonCurrent { get; set; }
    public string? CurrentIdentity { get; set; }
    public string CurrentState { get; set; } = "";
    public bool MissingHostRowSerialized { get; set; }
}

internal sealed class DelayedWriteReceipt
{
    public string Operation { get; set; } = "";
    public string IncumbentIdentity { get; set; } = "";
    public string SuccessorIdentity { get; set; } = "";
    public string SuccessorStateAtOldCommit { get; set; } = "";
    public int AdmissionBackendPid { get; set; }
    public bool AdmissionBackendTerminated { get; set; }
    public int BarrierBackendPid { get; set; }
    public int ProviderBackendPid { get; set; }
    public string? ProviderWaitEventType { get; set; }
    public string? ProviderWaitEvent { get; set; }
    public bool ProviderWasBlockedByBarrier { get; set; }
    public bool SuccessorRemapCommittedBeforeBarrierRelease { get; set; }
    public DateTime IncumbentStartTimeUtc { get; set; }
    public DateTime IncumbentIAmAliveTimeUtc { get; set; }
    public DateTime RemapLivenessDatabaseTimeUtc { get; set; }
    public bool IncumbentWasExpiredAtRemap { get; set; }
    public int CapturedTableVersion { get; set; }
    public string CapturedTableVersionEtag { get; set; } = "";
    public int MembershipVersionBefore { get; set; }
    public int MembershipVersionAfterRemapBeforeProviderCommit { get; set; }
    public int MembershipVersionAfterProviderCommit { get; set; }
    public bool ProviderCallCompleted { get; set; }
    public int BeforeStatus { get; set; }
    public int AfterStatus { get; set; }
    public DateTime BeforeIAmAliveTimeUtc { get; set; }
    public DateTime AfterIAmAliveTimeUtc { get; set; }
    public bool UnsafeCommitObserved { get; set; }
    public bool DesiredStaleWriteRefused { get; set; }
    public bool HeartbeatLeftMembershipTableVersionUnchanged { get; set; }
    public bool StatusTokenRemainedCurrentThroughRemap { get; set; }
}
