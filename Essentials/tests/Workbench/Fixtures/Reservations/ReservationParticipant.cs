using CShells.Features;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.Tests.Reservations;

[ShellFeature("ReservationWitness", DependsOn = new object[] { "ActivitiesCompensation" })]
public sealed class ReservationWitnessFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IActivityReconciliationSource, ReservationParticipantCatalog>();
}

// Feed packages contribute their catalog explicitly; the host's CLR source scans only its own binary folder.
public sealed class ReservationParticipantCatalog : IActivityReconciliationSource
{
    public string SourceId => "ReservationWitness";
    public string SourceKind => "OwnedProcessTest";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([
            new(null, "1.0.0", typeof(ReservationParticipant).FullName!, "Reservation participant", "Tests", null,
                "elsa.clr-activity", "1", WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ReservationParticipant))),
                [Input(nameof(ReservationParticipant.DirectoryPath), "String"), Input(nameof(ReservationParticipant.Key), "String"),
                    Input(nameof(ReservationParticipant.Reject), "Boolean"), Input(nameof(ReservationParticipant.HoldAfterEffect), "Boolean"),
                    Input(nameof(ReservationParticipant.Undoes), "String", isRequired: false),
                    Input(nameof(ReservationParticipant.LoseResponse), "Boolean", isRequired: false),
                    Input(nameof(ReservationParticipant.FailTimes), "Int32", isRequired: false)],
                [new OutputDefinition(ReservationResult.OutputKey, nameof(ReservationResult.Reservation), new TypeReference("String"), null,
                    nameof(ReservationResult.Reservation), null, IsNullable: false, IsRequired: true)], [])
        ]);

    private static InputDefinition Input(string name, string alias, bool isRequired = true) =>
        new(name, name, new TypeReference(alias), null, name, null, IsNullable: !isRequired, IsRequired: isRequired);
}

/// <summary>
/// An external participant whose state survives termination of its host, following the spec 251 participant
/// contract. A forward call applies its effect once per operation key; a compensation (an activity given a target
/// operation key) removes its target's effect, or records a tombstone when the effect is absent so a late forward
/// call is refused. It journals every call so a test can tell a repeated call from a repeated effect.
/// With <see cref="HoldAfterEffect"/> it applies the effect and then waits for a release file, so the test can kill the
/// host after the effect and before the activity's completion commits. With <see cref="LoseResponse"/> its first call
/// applies the effect and then reports <c>outcome-unknown</c>; with <see cref="FailTimes"/> its first calls fail before
/// any effect. Its result is a reservation id; a handler given that id through <see cref="Undoes"/> journals it.
/// </summary>
[ActivityOutcome("Done")]
public sealed partial class ReservationParticipant : Activity<ReservationResult>
{
    public const string RejectionCode = "order.rejected";

    [ActivityInput] public string DirectoryPath { get; set; } = null!;
    [ActivityInput] public string Key { get; set; } = null!;
    [ActivityInput] public bool Reject { get; set; }
    [ActivityInput] public bool HoldAfterEffect { get; set; }
    [ActivityInput] public string? Undoes { get; set; }
    [ActivityInput] public bool LoseResponse { get; set; }
    [ActivityInput] public int FailTimes { get; set; }

    public const string UnavailableCode = "reservation.unavailable";
    public const string TimeoutCode = "reservation.timeout";
    public const string RefusedCode = "reservation.refused";

    protected override async ValueTask<ActivityTransition<ReservationResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        Directory.CreateDirectory(DirectoryPath);
        var calls = Path.Combine(DirectoryPath, Key + ".calls");
        var previousCalls = File.Exists(calls) ? (await File.ReadAllLinesAsync(calls, context.CancellationToken)).Length : 0;
        await File.AppendAllTextAsync(calls,
            $"{Environment.ProcessId}|{context.InvocationId}|{context.AttemptId}|{context.OperationKey}{Environment.NewLine}", context.CancellationToken);

        if (Undoes is not null)
            await File.AppendAllTextAsync(Path.Combine(DirectoryPath, Key + ".undoes"), Undoes + Environment.NewLine, context.CancellationToken);

        if (Reject)
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault(RejectionCode, "The order was rejected."));

        if (previousCalls < FailTimes)
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault(UnavailableCode, "The reservation service is unavailable."));

        if (context.TargetOperationKey is { } target)
        {
            // A compensation: remove the target's effect, or remember it so a late forward call is refused.
            var targetMarker = EffectMarker(target);
            if (File.Exists(targetMarker))
                File.Delete(targetMarker);
            else if (!File.Exists(AppliedMarker(context.OperationKey)))
                await File.WriteAllTextAsync(TombstoneMarker(target), context.OperationKey, context.CancellationToken);

            if (!File.Exists(AppliedMarker(context.OperationKey)))
            {
                await File.WriteAllTextAsync(AppliedMarker(context.OperationKey), context.InvocationId, context.CancellationToken);
                await File.AppendAllTextAsync(Path.Combine(DirectoryPath, "effects.log"), Key + Environment.NewLine, context.CancellationToken);
            }
        }
        else
        {
            if (File.Exists(TombstoneMarker(context.OperationKey)))
                return ActivityTransition.Fault<ReservationResult>(new ActivityFault(RefusedCode, "The reservation was already compensated."));

            var marker = EffectMarker(context.OperationKey);
            if (!File.Exists(marker) && !File.Exists(AppliedMarker(context.OperationKey)))
            {
                await File.WriteAllTextAsync(marker, context.InvocationId, context.CancellationToken);
                await File.WriteAllTextAsync(AppliedMarker(context.OperationKey), context.InvocationId, context.CancellationToken);
                await File.AppendAllTextAsync(Path.Combine(DirectoryPath, "effects.log"), Key + Environment.NewLine, context.CancellationToken);
            }
        }

        while (HoldAfterEffect && !File.Exists(Path.Combine(DirectoryPath, Key + ".release")))
            await Task.Delay(25, context.CancellationToken);

        if (LoseResponse && previousCalls == 0)
            return ActivityTransition.Fault<ReservationResult>(new ActivityFault(TimeoutCode, "No response from the reservation service.", category: "outcome-unknown"));

        return ActivityTransition.Complete(new ReservationResult($"{Key}:{context.InvocationId}"));
    }
}

public sealed partial class ReservationParticipant
{
    // A present effect: deleted when compensated. The applied marker keeps a repeat from re-applying after an undo.
    private string EffectMarker(string operationKey) => Path.Combine(DirectoryPath, $"{FileSafe(operationKey)}.effect");
    private string AppliedMarker(string operationKey) => Path.Combine(DirectoryPath, $"{FileSafe(operationKey)}.applied");
    private string TombstoneMarker(string operationKey) => Path.Combine(DirectoryPath, $"{FileSafe(operationKey)}.tombstone");
    private static string FileSafe(string operationKey) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationKey)))[..24];
}

/// <summary>The reservation a participant made: its key and the invocation that made it.</summary>
public sealed record ReservationResult([property: Output(Key = ReservationResult.OutputKey, Path = "reservation")] string Reservation)
{
    public const string OutputKey = "Reservation";
}
