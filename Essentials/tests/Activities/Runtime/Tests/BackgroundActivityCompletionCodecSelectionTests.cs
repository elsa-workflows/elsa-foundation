using System.Text.Json;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 1208 FR-003: a background completion's outcome selection crosses the worker boundary unchanged in every state.
/// The worker serializes the result and records the selection on the terminal result; the coordinator rehydrates the
/// completion from the receipt. Unspecified, several names in order, and explicitly empty each come back as they went.
/// </summary>
public sealed class BackgroundActivityCompletionCodecSelectionTests : IDisposable
{
    private const int MaximumBytes = 4096;
    private readonly ServiceProvider _services;
    private readonly BackgroundActivityCompletionCodec _codec;
    private readonly ActivityContract _contract;

    public BackgroundActivityCompletionCodecSelectionTests()
    {
        var services = new ServiceCollection();
        new SerializationFeature().ConfigureServices(services);
        _services = services.BuildServiceProvider();
        var types = _services.GetRequiredService<IWellKnownTypeRegistry>();
        var resultAlias = TypeAliasConvention.CanonicalAlias(typeof(int));
        types.RegisterType(typeof(int), resultAlias);
        _codec = new BackgroundActivityCompletionCodec(types);
        _contract = new ActivityContract(
            "Test.Background",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { }),
            [],
            new ActivityResultContract(new ValueTypeDescriptor(resultAlias), true, ActivityValuePolicy.Default, []),
            ["Done", "A", "B"],
            new ActivityActivationRequirement("test", "Test.Background"),
            SideEffectProfile.External,
            supportsBackgroundExecution: true);
    }

    public void Dispose() => _services.Dispose();

    public static TheoryData<OutcomeSelection> Selections() =>
        [OutcomeSelection.Unspecified, OutcomeSelection.Named("A", "B"), OutcomeSelection.Named("B", "A"), OutcomeSelection.Empty];

    [Theory]
    [MemberData(nameof(Selections))]
    public void Every_selection_state_crosses_the_worker_boundary_unchanged(OutcomeSelection selection)
    {
        var completion = (IActivityCompletionTransition)ActivityTransition.Complete(42, selection);

        var json = _codec.Serialize(_contract, completion, MaximumBytes);
        var receipt = RoundTrip(new BackgroundActivityWorkTerminalResult(BackgroundActivityWorkResultKind.Complete, completion.Selection, json));
        var rehydrated = Assert.IsAssignableFrom<IActivityCompletionTransition>(
            _codec.Deserialize(_contract, receipt.ResultJson!, receipt.Selection!, MaximumBytes));

        Assert.Equal(selection, receipt.Selection);
        Assert.Equal(selection, rehydrated.Selection);
        Assert.Equal(selection.ResolveNames(), rehydrated.Selection.ResolveNames());
        Assert.Equal(42, rehydrated.Result);
    }

    [Fact]
    public void A_selection_outside_the_pinned_outcome_set_is_refused_on_both_sides()
    {
        var completion = (IActivityCompletionTransition)ActivityTransition.Complete(42, OutcomeSelection.Named("C"));

        Assert.Contains("BG-ACT-005", Assert.Throws<BackgroundActivityValidationException>(() => _codec.Serialize(_contract, completion, MaximumBytes)).Message);
        Assert.Contains("BG-ACT-005", Assert.Throws<BackgroundActivityValidationException>(() =>
            _codec.Deserialize(_contract, "42", OutcomeSelection.Named("C"), MaximumBytes)).Message);
    }

    // The background work store keeps its rows with the Web serializer defaults.
    private static BackgroundActivityWorkTerminalResult RoundTrip(BackgroundActivityWorkTerminalResult result)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<BackgroundActivityWorkTerminalResult>(JsonSerializer.Serialize(result, options), options)!;
    }
}
