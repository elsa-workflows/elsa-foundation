using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilityEvaluatorTests
{
    private readonly ActivityDefinitionModel _writeLine = Activity("write-line", "Elsa.Primitives.WriteLine");
    private readonly ActivityDefinitionModel _sendHttp = Activity("send-http", "Elsa.Http.SendHttpRequest");
    private readonly ActivityDefinitionModel _runJavaScript = Activity("run-js", "Elsa.Scripting.RunJavaScript");

    [Fact]
    public void Evaluate_NoRules_ReturnsAllActivities()
    {
        var result = Evaluate(new ActivityAvailabilityOptions());

        Assert.Equal(
            ["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest", "Elsa.Scripting.RunJavaScript"],
            result);
    }

    [Fact]
    public void Evaluate_ExcludeActivityType_RemovesMatchingActivity()
    {
        var options = new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Scripting.RunJavaScript"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal(["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest"], result);
    }

    [Fact]
    public void Evaluate_IncludeActivityType_ReturnsOnlyMatchingActivity()
    {
        var options = new ActivityAvailabilityOptions
        {
            Include = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Http.SendHttpRequest"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal(["Elsa.Http.SendHttpRequest"], result);
    }

    [Fact]
    public void Evaluate_IncludeSet_ExpandsExplicitActivityTypes()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Core"] = ["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest"]
            },
            Include = new ActivityAvailabilityRuleSet
            {
                Sets = ["Core"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal(["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest"], result);
    }

    [Fact]
    public void Evaluate_SetEntry_DoesNotMatchByPrefix()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Http"] = ["Elsa.Http"]
            },
            Include = new ActivityAvailabilityRuleSet
            {
                Sets = ["Http"]
            }
        };

        var result = Evaluate(options);

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_ExcludeWinsOverInclude()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Core"] = ["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest"]
            },
            Include = new ActivityAvailabilityRuleSet
            {
                Sets = ["Core"]
            },
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = ["Elsa.Http.SendHttpRequest"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal(["Elsa.Primitives.WriteLine"], result);
    }

    [Fact]
    public void Evaluate_UnknownSet_DoesNotRemoveUnrelatedActivities()
    {
        var options = new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                Sets = ["Missing"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal(
            ["Elsa.Primitives.WriteLine", "Elsa.Http.SendHttpRequest", "Elsa.Scripting.RunJavaScript"],
            result);
    }

    private IReadOnlyList<string> Evaluate(ActivityAvailabilityOptions options)
    {
        var evaluator = new DefaultActivityAvailabilityEvaluator(options);
        return evaluator
            .FilterAddable([_writeLine, _sendHttp, _runJavaScript])
            .Select(x => x.ActivityTypeKey)
            .ToArray();
    }

    private static ActivityDefinitionModel Activity(string id, string activityTypeKey) =>
        new(id, activityTypeKey, "Test", activityTypeKey, null);
}
