using System.Reflection;
using Xunit;

namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// A drive: one or more real workflow executions that exercise a single activity and let the recorder read what
/// the engine actually committed. One implementation per activity, discovered by reflection so adding an activity
/// without adding a drive fails the coverage assertions rather than passing silently.
/// </summary>
public interface IActivityDrive
{
    /// <summary>The activity this drive exercises.</summary>
    Type ActivityType { get; }

    Task DriveAsync(ActivityDriveRecorder recorder);
}

/// <summary>
/// Runs every <see cref="IActivityDrive"/> in this assembly exactly once, then exposes the observations for the
/// coverage assertions. Drives run in the fixture rather than as individual tests because the assertions are
/// <em>cross-drive</em>: "every declared outcome of every shipped activity was reached" cannot be evaluated until
/// all of them have run, and xUnit gives no way to order a test after the rest.
/// </summary>
/// <remarks>
/// A drive that throws does not abort the pass: its failure is captured and surfaced by
/// <c>ActivityDriveHealthTests</c>, so one broken drive reports as one failure instead of masking the coverage
/// picture for the other 27 activities.
/// </remarks>
public sealed class ActivityDriveFixture : IAsyncLifetime
{
    private readonly Dictionary<Type, Exception> _failures = [];

    public ActivityDriveRecorder Recorder { get; } = new();

    /// <summary>Every drive implementation in this assembly, in stable name order.</summary>
    public static IReadOnlyList<IActivityDrive> Drives { get; } = typeof(ActivityDriveFixture).Assembly
        .GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IActivityDrive).IsAssignableFrom(type))
        .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .Select(type => (IActivityDrive)Activator.CreateInstance(type)!)
        .ToArray();

    /// <summary>The exception a drive threw, or null when it completed.</summary>
    public Exception? FailureFor(Type activityType) => _failures.GetValueOrDefault(activityType);

    public async Task InitializeAsync()
    {
        foreach (var drive in Drives)
        {
            try
            {
                await drive.DriveAsync(Recorder);
            }
            catch (Exception exception)
            {
                _failures[drive.ActivityType] = exception;
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class ActivityDriveCollection : ICollectionFixture<ActivityDriveFixture>
{
    public const string Name = "activity-drive";
}

/// <summary>Shared base for the coverage assertions: they all read one fixture's observations.</summary>
[Collection(ActivityDriveCollection.Name)]
public abstract class ActivityDriveTestBase(ActivityDriveFixture fixture)
{
    protected ActivityDriveFixture Fixture { get; } = fixture;

    protected ActivityDriveRecorder Recorder => Fixture.Recorder;

    /// <summary>Resolves a display name to the activity type, for readable <c>[MemberData]</c> case names.</summary>
    protected static Type Activity(string activityTypeName) =>
        ActivityContractInventory.Activities.Single(type => type.FullName == activityTypeName);

    /// <summary>Drives are keyed by activity full name in theory data so failures name the activity, not an index.</summary>
    /// <param name="excluded">
    /// Returns a reason when the activity is deliberately not driven here. Excluded cases are left out of the
    /// enumerated universe rather than asserted-and-skipped: xunit 2.9's runner does not surface a dynamic skip,
    /// so an excluded case would otherwise read as an ordinary failure. The exclusions are reported instead by
    /// <c>ActivityDriveHealthTests.Undriven_coverage_is_declared_with_reasons</c>.
    /// </param>
    protected static TheoryData<string> ActivityNames(Func<string, string?>? excluded = null)
    {
        var data = new TheoryData<string>();
        foreach (var type in ActivityContractInventory.Activities)
            if (excluded?.Invoke(type.FullName!) is null)
                data.Add(type.FullName!);
        return data;
    }

    /// <inheritdoc cref="ActivityNames"/>
    protected static TheoryData<string, string> ActivityMemberPairs(
        Func<Type, IReadOnlyList<string>> members,
        Func<string, string, string?>? excluded = null)
    {
        var data = new TheoryData<string, string>();
        foreach (var type in ActivityContractInventory.Activities)
            foreach (var member in members(type))
                if (excluded?.Invoke(type.FullName!, member) is null)
                    data.Add(type.FullName!, member);
        return data;
    }
}
