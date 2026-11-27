using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Services.Executables;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Actor = Kernel.WorkflowActor.WorkflowActor;
using ContractAdmission = Kernel.Foundation.Contracts.Admission;
using Native = Kernel.Native;
using StartSpec = Kernel.Foundation.Contracts.StartSpec;

namespace Kernel.Native.Run;

internal static class KernelScheduleReconciliationChecks
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 11, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Unqualified = new(new(PersistenceScope.DefaultValue), null);
    private static readonly TriggerScope Named = Unqualified with { TenantId = PersistenceScope.DefaultValue };
    private static readonly ActorRef Schedule = new(new string('A', 64));
    private static readonly ActorRef Birth = new(new string('B', 64));
    private static readonly string Timer = "~" + new string('C', 64);
    private const string Key = "schedule:definition:trigger";

    internal static async Task<int> RunAsync()
    {
        var executable = ExecutableOf("artifact", "definition", Node("trigger", Types.Timer, [Literal("Interval", "PT1S", "String")]));
        var material = new InMemoryWorkflowExecutableStore();
        await material.SaveAsync(executable);
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var slotId = WorkflowActivationSlotIdentity.Create("definition", "Green");
        var sourceId = WorkflowActivationReferenceIdentity.Create("publication");
        await references.SaveAsync(new(sourceId, "artifact", "definition", "definition", "1", "definition",
            executable.Identity.DefinitionVersionId, "1", Now, Now, WorkflowExecutableReferenceScope.Published,
            ActivationId: "publication", SlotId: slotId, TenantId: Named.TenantId));
        var active = new RuntimeKernelScheduleControl(Named, Key, "definition", "trigger", 4,
            new(slotId, "Green", "publication", 1, sourceId, executable.Identity));
        var stop = active with { Scope = Unqualified, Owner = null, Revision = 3 };
        var foreign = stop with { Scope = new(new("foreign"), "foreign") };
        var pinned = Actor.Start(Native.NativeExecutionModel.Start("artifact", triggerNode: "trigger",
            trigger: System.Text.Json.Nodes.JsonValue.Create(Key), definition: "definition"));
        var older = Offer(Unqualified, 5);
        var named = Offer(Named, 6);
        var malformed = Offer(Unqualified, 7) with { PinnedStart = "{}" };
        var released = Offer(Unqualified, 8);
        var dependent = Offer(Named, 9);
        var fixture = new Fixture(active, stop, foreign, older, named, malformed, released, dependent);
        var services = new ServiceCollection()
            .AddScoped(_ => new Access())
            .AddScoped<IPersistenceAccessContextAccessor>(sp => sp.GetRequiredService<Access>())
            .AddScoped<IPersistenceAccessContextBinder>(sp => sp.GetRequiredService<Access>())
            .AddScoped(sp => new RuntimeLedger(fixture, sp.GetRequiredService<Access>()))
            .AddScoped<IRuntimeKernelScheduleControlReader>(sp => sp.GetRequiredService<RuntimeLedger>())
            .AddScoped<IRuntimeKernelScheduleOccurrenceDeliveryStore>(sp => sp.GetRequiredService<RuntimeLedger>())
            .AddScoped<IRuntimeKernelScheduleOccurrenceRetirementStore>(sp => sp.GetRequiredService<RuntimeLedger>())
            .AddScoped<IRuntimeKernelScheduleOccurrenceStore>(sp => sp.GetRequiredService<RuntimeLedger>())
            .AddSingleton<IWorkflowExecutableStore>(material)
            .AddSingleton<IWorkflowExecutableSourceReferenceStore>(references);
        await using var runtime = services.BuildServiceProvider(validateScopes: true);
        var clock = new PulseClock();
        var log = new RecordingLogger();
        var provider = new KernelScheduleProvider(Apply, Admit, Confirm);
        var worker = new KernelScheduleReconciliationWorker(runtime.GetRequiredService<IServiceScopeFactory>(), Named, ContractScopes.Bind("default"),
            KernelHostCapabilities.All, null, provider, _ => ValueTask.FromResult(Now.ToUnixTimeMilliseconds()), clock, log);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var count = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var running = worker.RunAsync(count.Token);
        try
        {
            await fixture.InitialPass.Task.WaitAsync(deadline.Token);
            try
            {
                await worker.RunAsync(count.Token);
                throw new Exception("A second loop was allowed for one shell.");
            }
            catch (InvalidOperationException) { }
            Require(fixture.Controls.SequenceEqual([(Unqualified, (string?)null)]), "controls were read once through the normalized scope");
            Require(fixture.Deliveries.Select(read => read.Scope).Distinct().ToHashSet().SetEquals([Unqualified, Named]),
                "both exact default ledger aliases were scanned");
            Require(fixture.Retirements.SetEquals([released]) && fixture.Proofs.SetEquals([released, dependent]),
                "only retirement with irreversible proof and released dependents dropped its pin");
            Require(fixture.Header is { Revision: 4, Authority: { } } header &&
                    await worker.CreateAcceptor().TryAcceptAsync(new("default", Schedule, 10, Timer, Now.ToUnixTimeMilliseconds(),
                        Now.AddMinutes(1).ToUnixTimeMilliseconds(), header.Authority), deadline.Token) &&
                    fixture.Accepted is { Scope: var acceptedScope } && acceptedScope == Named,
                "the composed acceptor uses a fresh shell scope and preserves the exact named publication alias");

            clock.Advance(TimeSpan.FromMinutes(2));
            await fixture.BoundedPageFinished.Task.WaitAsync(deadline.Token);
            clock.Advance(TimeSpan.FromMinutes(2));
            await fixture.RetryPass.Task.WaitAsync(deadline.Token);
            Require(fixture.Admissions.GetValueOrDefault(older.Id) == 2 && fixture.Marks[older.Id] == Birth.Id &&
                    fixture.Marks[named.Id] == Birth.Id && fixture.Admissions.GetValueOrDefault(malformed.Id) == 0,
                "unknown acknowledgement retries the same durable birth, and a malformed pin does not block its page");
            var deliveryPass = fixture.Deliveries.Where(read => read.Scope == Unqualified).Take(3).ToArray();
            Require(deliveryPass.Length == 3 && deliveryPass[0].After is null && deliveryPass[1].After is not null &&
                    deliveryPass[2].After is null &&
                    deliveryPass[0].Through == deliveryPass[1].Through &&
                    deliveryPass[2].Through.AcceptedAtUtcTicks > deliveryPass[1].Through.AcceptedAtUtcTicks,
                "a fixed tail ends a pass despite sustained full live pages and an older failed acknowledgement is retried");
            Require(log.Messages.Any(message => message.Contains("superseded", StringComparison.Ordinal)) &&
                    log.Messages.Any(message => message.Contains("different durable intent", StringComparison.Ordinal)) &&
                    log.Messages.Any(message => message.Contains("tenant or exact target", StringComparison.Ordinal)),
                "superseded, conflicted and wrong-tenant controls have distinct diagnostics");
            await fixture.RepeatControl.Task.WaitAsync(deadline.Token);
            Require(fixture.Controls.Take(3).SequenceEqual([(Unqualified, (string?)null), (Unqualified, "second-page"), (Unqualified, (string?)null)]) &&
                    fixture.ControlAttempts == 2 && fixture.ControlWrites == 1,
                "a fresh control sweep starts over and consumes the provider's duplicate receipt without another control write");
        }
        finally
        {
            await count.CancelAsync();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Require(log.Errors.IsEmpty, "ordinary cancellation does not report a schedule fault");
        var failure = new InvalidOperationException("Schedule timer creation failed.");
        var faulted = new KernelScheduleReconciliationWorker(runtime.GetRequiredService<IServiceScopeFactory>(), Named,
            ContractScopes.Bind("default"), KernelHostCapabilities.All, null, provider,
            _ => ValueTask.FromResult(Now.ToUnixTimeMilliseconds()), new FailingTimerClock(failure), log);
        await faulted.RunAsync(deadline.Token);
        Require(log.Errors.TryDequeue(out var reported) && ReferenceEquals(reported, failure),
            "an escaping schedule fault is reported immediately, without waiting for shell shutdown");
        Console.WriteLine("ok schedule reconciliation settles scoped control, admission, replay and retirement");
        return 0;

        RuntimeKernelScheduleOccurrence Offer(TriggerScope scope, long generation) =>
            new(scope, Key, Schedule.Id, "Green", "publication", 1, generation, Timer, Now, Now.AddMinutes(1),
                executable.Identity, "trigger", JsonSerializer.Serialize(pinned));

        ValueTask<ContractAdmission> Apply(RuntimeKernelScheduleControl control, ScheduleHeader? header, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (control == active)
            {
                Require(header is { Revision: 4, Authority: not null }, "active control must bind the exact scoped start");
                fixture.Header = header;
                if (++fixture.ControlAttempts > 1)
                {
                    fixture.RepeatControl.TrySetResult();
                    return ValueTask.FromResult(new ContractAdmission(AdmissionOutcome.Duplicate));
                }
                fixture.ControlWrites++;
                return ValueTask.FromResult(new ContractAdmission(AdmissionOutcome.Accepted));
            }
            Require(header is null, "Stop has no managed start header");
            return ValueTask.FromResult(new ContractAdmission(AdmissionOutcome.Refused,
                Reason: control == stop ? RefusalReason.StaleSelection : RefusalReason.Conflict));
        }

        ValueTask<ContractAdmission> Admit(RuntimeKernelScheduleOccurrence item, StartSpec start, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require((item == older || item == named) && start == pinned, "relay supplies the exact retained actor start");
            var attempts = fixture.Admissions.AddOrUpdate(item.Id, 1, (_, previous) => previous + 1);
            return ValueTask.FromResult(new ContractAdmission(attempts == 1 ? AdmissionOutcome.Accepted : AdmissionOutcome.Duplicate, Birth));
        }

        ValueTask<bool> Confirm(RuntimeKernelScheduleOccurrence item, ActorRef actor, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(actor == Birth && (item == released || item == dependent), "retirement proof receives the exact immutable receipt and birth");
            fixture.Proofs.Add(item);
            return ValueTask.FromResult(item == released);
        }
    }

    private static void Require(bool result, string name)
    {
        if (!result) throw new InvalidOperationException(name);
    }

    private sealed class Fixture(RuntimeKernelScheduleControl active, RuntimeKernelScheduleControl stop, RuntimeKernelScheduleControl foreign,
        RuntimeKernelScheduleOccurrence older, RuntimeKernelScheduleOccurrence named, RuntimeKernelScheduleOccurrence malformed,
        RuntimeKernelScheduleOccurrence released, RuntimeKernelScheduleOccurrence dependent)
    {
        internal RuntimeKernelScheduleControl Active => active;
        internal RuntimeKernelScheduleControl Stop => stop;
        internal RuntimeKernelScheduleControl Foreign => foreign;
        internal RuntimeKernelScheduleOccurrence Older => older;
        internal RuntimeKernelScheduleOccurrence NamedOffer => named;
        internal RuntimeKernelScheduleOccurrence Malformed => malformed;
        internal RuntimeKernelScheduleOccurrence Released => released;
        internal RuntimeKernelScheduleOccurrence Dependent => dependent;
        internal readonly List<(TriggerScope Scope, string? After)> Controls = [];
        internal readonly List<(TriggerScope Scope, RuntimeKernelScheduleOccurrenceCursor? After, RuntimeKernelScheduleOccurrenceCursor Through)> Deliveries = [];
        internal readonly HashSet<RuntimeKernelScheduleOccurrence> Proofs = [];
        internal readonly HashSet<RuntimeKernelScheduleOccurrence> Retirements = [];
        internal readonly ConcurrentDictionary<string, int> Admissions = new();
        internal readonly ConcurrentDictionary<string, string> Marks = new();
        internal readonly TaskCompletionSource InitialPass = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource BoundedPageFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RetryPass = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RepeatControl = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ScheduleHeader? Header;
        internal RuntimeKernelScheduleOccurrence? Accepted;
        internal bool LostMark;
        internal int PendingCount = 33;
        internal bool SawBoundedPage;
        internal int ControlAttempts;
        internal int ControlWrites;
    }

    private sealed class RuntimeLedger(Fixture fixture, Access access) : IRuntimeKernelScheduleControlReader,
        IRuntimeKernelScheduleOccurrenceDeliveryStore, IRuntimeKernelScheduleOccurrenceRetirementStore, IRuntimeKernelScheduleOccurrenceStore
    {
        private int _calls;

        public ValueTask<RuntimeKernelScheduleControl?> FindAsync(TriggerScope scope, string scheduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string?> ReadTailAsync(TriggerScope scope, CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            return ValueTask.FromResult<string?>("tail");
        }

        public ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, int limit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, string through, int limit,
            CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            Require(through == "tail", "control pages use the selected tail");
            fixture.Controls.Add((scope, after));
            return ValueTask.FromResult(after is null
                ? new RuntimeKernelScheduleControlPage([fixture.Active, fixture.Stop, fixture.Foreign], "second-page")
                : new RuntimeKernelScheduleControlPage([fixture.Stop with { Revision = 2 }], null));
        }

        public ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadPendingTailAsync(TriggerScope scope, CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            if (scope != Named) return ValueTask.FromResult<RuntimeKernelScheduleOccurrenceCursor?>(PendingAt(fixture.PendingCount).Cursor);
            if (fixture.SawBoundedPage) fixture.BoundedPageFinished.TrySetResult();
            return ValueTask.FromResult(fixture.Marks.ContainsKey(fixture.NamedOffer.Id) ? null : Cursor(fixture.NamedOffer));
        }

        public ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope, RuntimeKernelScheduleOccurrenceCursor? after,
            int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>> ListPendingAsync(TriggerScope scope, RuntimeKernelScheduleOccurrenceCursor? after,
            RuntimeKernelScheduleOccurrenceCursor through, int limit, CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            fixture.Deliveries.Add((scope, after, through));
            if (scope == Named)
                return ValueTask.FromResult<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>>([new(fixture.NamedOffer, Cursor(fixture.NamedOffer))]);

            fixture.PendingCount += limit;
            var live = Enumerable.Range(1, fixture.PendingCount).Select(PendingAt)
                .Where(item => after is null || item.Cursor.AcceptedAtUtcTicks > after.AcceptedAtUtcTicks)
                .Where(item => !fixture.Marks.ContainsKey(item.Occurrence.Id)).Take(limit).ToArray();
            var page = live.Where(item => item.Cursor.AcceptedAtUtcTicks <= through.AcceptedAtUtcTicks).ToArray();
            if (after is not null && page.Length < limit)
            {
                Require(live.Length == limit, "an unbounded query would still receive a full page from the changing ledger");
                fixture.SawBoundedPage = true;
            }
            return ValueTask.FromResult<IReadOnlyList<RuntimeKernelScheduleOccurrenceDelivery>>(page);
        }

        private RuntimeKernelScheduleOccurrenceDelivery PendingAt(int position)
        {
            var item = position switch
            {
                1 => fixture.Older,
                2 => fixture.Malformed,
                _ => fixture.Malformed with { ArmGeneration = 100 + position }
            };
            return new(item, new(Now.UtcTicks + position, item.Id));
        }

        public ValueTask MarkMaterializedAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference, CancellationToken cancellationToken = default)
        {
            CheckScope(occurrence.Scope);
            if (occurrence == fixture.Older && !fixture.LostMark)
            {
                fixture.LostMark = true;
                throw new IOException("the first Runtime acknowledgement was lost");
            }
            Require(fixture.Marks.TryAdd(occurrence.Id, actorReference), "only an unacknowledged occurrence is marked");
            if (occurrence == fixture.Older) fixture.RetryPass.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<RuntimeKernelScheduleMaterializedOccurrence>> ListMaterializedAsync(TriggerScope scope,
            RuntimeKernelScheduleOccurrenceCursor? after, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RuntimeKernelScheduleOccurrenceCursor?> ReadMaterializedTailAsync(TriggerScope scope, CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            var item = scope == Named ? fixture.Dependent : fixture.Released;
            return ValueTask.FromResult(fixture.Retirements.Contains(item) ? null : Cursor(item));
        }

        public ValueTask<IReadOnlyList<RuntimeKernelScheduleMaterializedOccurrence>> ListMaterializedAsync(TriggerScope scope,
            RuntimeKernelScheduleOccurrenceCursor? after, RuntimeKernelScheduleOccurrenceCursor through, int limit,
            CancellationToken cancellationToken = default)
        {
            CheckScope(scope);
            var item = scope == Named ? fixture.Dependent : fixture.Released;
            Require(through == Cursor(item), "retirement pages use the selected tail");
            return ValueTask.FromResult<IReadOnlyList<RuntimeKernelScheduleMaterializedOccurrence>>(
                fixture.Retirements.Contains(item) ? [] : [new(item, Birth.Id, Cursor(item))]);
        }

        public async ValueTask<bool> TryRetireAsync(RuntimeKernelScheduleOccurrence occurrence, string actorReference,
            Func<RuntimeKernelScheduleOccurrence, string, CancellationToken, ValueTask<bool>> confirmRetired, CancellationToken cancellationToken = default)
        {
            CheckScope(occurrence.Scope);
            var confirmed = await confirmRetired(occurrence, actorReference, cancellationToken);
            if (confirmed) fixture.Retirements.Add(occurrence);
            if (occurrence == fixture.Dependent) fixture.InitialPass.TrySetResult();
            return confirmed;
        }

        public async ValueTask<bool> TryAcceptAsync(RuntimeKernelScheduleOccurrence occurrence, Func<CancellationToken, ValueTask<DateTimeOffset>> readScheduleClock,
            CancellationToken cancellationToken = default)
        {
            CheckScope(occurrence.Scope);
            Require(await readScheduleClock(cancellationToken) == Now, "the Runtime acceptor samples the live Foundation clock");
            fixture.Accepted = occurrence;
            return true;
        }

        private void CheckScope(TriggerScope scope) =>
            Require(access.Current.RequireScope() == scope.Persistence && ++_calls == 1, "every Runtime operation uses a fresh bound shell scope");

        private static RuntimeKernelScheduleOccurrenceCursor Cursor(RuntimeKernelScheduleOccurrence occurrence) => new(Now.UtcTicks, occurrence.Id);
    }

    private sealed class Access : IPersistenceAccessContextBinder, IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; private set; } = PersistenceAccessContext.Global;
        public void Bind(PersistenceAccessContext context) => Current = context;
    }

    private sealed class PulseClock : TimeProvider
    {
        private long _time;
        private PulseTimer? _timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Now.AddTicks(GetTimestamp());
        public override long GetTimestamp() => Volatile.Read(ref _time);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new PulseTimer(callback, state);

        internal void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _time, elapsed.Ticks);
            _timer?.Pulse();
        }

        private sealed class PulseTimer(TimerCallback tick, object? state) : ITimer
        {
            private TimerCallback? _tick = tick;
            internal void Pulse() => Volatile.Read(ref _tick)?.Invoke(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => _tick is not null;
            public void Dispose() => Interlocked.Exchange(ref _tick, null);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FailingTimerClock(Exception failure) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw failure;
    }

    private sealed class RecordingLogger : ILogger<KernelScheduleReconciliationWorker>
    {
        internal readonly ConcurrentQueue<string> Messages = new();
        internal readonly ConcurrentQueue<Exception> Errors = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception));
            if (logLevel == LogLevel.Error && exception is not null) Errors.Enqueue(exception);
        }
    }
}
