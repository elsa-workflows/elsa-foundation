using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Xunit;

namespace Elsa.Samples.Nuplane.Notes.Activities.Tests;

/// <summary>
/// What each release contributes to the activity catalog: its own version of the one activity, under one type key, so the
/// catalog keeps the earlier version beside the new one, and a node pinned to the earlier one still finds it.
/// </summary>
public sealed class NotesActivityReconciliationSourceTests : IDisposable
{
    private readonly NotesActivitiesRelease1 _release1 = new();

    [Fact]
    public async Task Release_1_0_0_reports_version_1_0_0_with_a_required_Text()
    {
        var activity = await ReadAsync(_release1.ReconciliationSource());

        Assert.Equal("1.0.0", activity.Version);
        Assert.Equal(typeof(AddNote).FullName, activity.ActivityTypeKey);
        Assert.Equal([("Text", true)], activity.Inputs.Select(input => (input.Name, input.IsRequired)));
    }

    [Fact]
    public async Task Release_1_1_0_reports_version_1_1_0_with_a_required_Text_and_optional_Tags()
    {
        var activity = await ReadAsync(new NotesActivityReconciliationSource());

        Assert.Equal("1.1.0", activity.Version);
        Assert.Equal(typeof(AddNote).FullName, activity.ActivityTypeKey);
        Assert.Equal([("Text", true), ("Tags", false)], activity.Inputs.Select(input => (input.Name, input.IsRequired)));
    }

    public void Dispose() => _release1.Dispose();

    private static async Task<ActivityVersionReconciliationModel> ReadAsync(IActivityReconciliationSource source) =>
        Assert.Single(await source.Read(CancellationToken.None));
}
