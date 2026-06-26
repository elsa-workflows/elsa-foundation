using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class ActivityAvailabilityEvaluatorTests
{
    private const string WriteLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string SendHttpTypeKey = "Elsa.Activities.Http.Activities.SendHttpRequest";
    private const string RunJavaScriptTypeKey = "Elsa.Activities.Scripting.Activities.RunJavaScript";

    private readonly ActivityDefinitionModel _writeLine = Activity("write-line", WriteLineTypeKey);
    private readonly ActivityDefinitionModel _sendHttp = Activity("send-http", SendHttpTypeKey);
    private readonly ActivityDefinitionModel _runJavaScript = Activity("run-js", RunJavaScriptTypeKey);

    [Fact]
    public void Evaluate_NoRules_ReturnsAllActivities()
    {
        var result = Evaluate(new ActivityAvailabilityOptions());

        Assert.Equal(
            [WriteLineTypeKey, SendHttpTypeKey, RunJavaScriptTypeKey],
            result);
    }

    [Fact]
    public void Evaluate_ExcludeActivityType_RemovesMatchingActivity()
    {
        var options = new ActivityAvailabilityOptions
        {
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [RunJavaScriptTypeKey]
            }
        };

        var result = Evaluate(options);

        Assert.Equal([WriteLineTypeKey, SendHttpTypeKey], result);
    }

    [Fact]
    public void Evaluate_IncludeActivityType_ReturnsOnlyMatchingActivity()
    {
        var options = new ActivityAvailabilityOptions
        {
            Include = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [SendHttpTypeKey]
            }
        };

        var result = Evaluate(options);

        Assert.Equal([SendHttpTypeKey], result);
    }

    [Fact]
    public void Evaluate_IncludeSet_ExpandsExplicitActivityTypes()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Core"] = [WriteLineTypeKey, SendHttpTypeKey]
            },
            Include = new ActivityAvailabilityRuleSet
            {
                Sets = ["Core"]
            }
        };

        var result = Evaluate(options);

        Assert.Equal([WriteLineTypeKey, SendHttpTypeKey], result);
    }

    [Fact]
    public void Evaluate_SetEntry_DoesNotMatchByPrefix()
    {
        var options = new ActivityAvailabilityOptions
        {
            Sets =
            {
                ["Http"] = ["Elsa.Activities.Http"]
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
                ["Core"] = [WriteLineTypeKey, SendHttpTypeKey]
            },
            Include = new ActivityAvailabilityRuleSet
            {
                Sets = ["Core"]
            },
            Exclude = new ActivityAvailabilityRuleSet
            {
                ActivityTypes = [SendHttpTypeKey]
            }
        };

        var result = Evaluate(options);

        Assert.Equal([WriteLineTypeKey], result);
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
            [WriteLineTypeKey, SendHttpTypeKey, RunJavaScriptTypeKey],
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
