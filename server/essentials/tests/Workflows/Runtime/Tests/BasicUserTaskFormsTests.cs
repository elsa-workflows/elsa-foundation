using System.Text.Json;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.UserTasks;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class BasicUserTaskFormsTests
{
    private const string ControlId = "example.rating/1";

    [Fact]
    public void Custom_control_definition_uses_one_exact_validator_and_its_configuration_rules()
    {
        var form = Form();
        new BasicUserTaskForms([new RatingValidator()]).ValidateDefinition(form);

        Assert.Throws<UserTaskException>(() => new BasicUserTaskForms().ValidateDefinition(form));
        Assert.Throws<UserTaskException>(() => new BasicUserTaskForms([new RatingValidator(), new RatingValidator()]).ValidateDefinition(form));
        Assert.Throws<UserTaskException>(() => new BasicUserTaskForms([new RatingValidator()]).ValidateDefinition(form with
        {
            Fields = [form.Fields[0] with { Kind = "string" }]
        }));
        Assert.Throws<UserTaskException>(() => new BasicUserTaskForms([new RatingValidator()]).ValidateDefinition(form with
        {
            Fields = [form.Fields[0] with { Configuration = Json("[]") }]
        }));
    }

    [Fact]
    public void Custom_control_response_validation_preserves_common_required_and_read_only_guards()
    {
        var forms = new BasicUserTaskForms([new RatingValidator()]);
        var form = Form();
        var invalid = forms.ValidateResponse(form, Json("{}"), Json("{\"rating\":6}"), requireFields: true);
        Assert.Equal("Rating must be between 1 and 5.", invalid["rating"]);

        var readOnlyForm = form with { Fields = [form.Fields[0] with { ReadOnly = true }] };
        var readOnly = forms.ValidateResponse(readOnlyForm, Json("{\"rating\":3}"), Json("{\"rating\":4}"), requireFields: false);
        Assert.Equal("This value cannot be changed.", readOnly["rating"]);

        var requiredForm = form with { Fields = [form.Fields[0] with { Required = true }] };
        var required = forms.ValidateResponse(requiredForm, Json("{}"), Json("{}"), requireFields: true);
        Assert.Equal("This field is required.", required["rating"]);
        Assert.Empty(forms.ValidateResponse(requiredForm, Json("{}"), Json("{\"rating\":3}"), requireFields: true));
    }

    private static UserTaskForm Form() => new("survey", "1", "Survey", UserTaskProtocol.BasicForms,
        [new UserTaskField("rating", "Rating", "number", Required: false) { ControlId = ControlId, Configuration = Json("{\"maximum\":5}") }]);

    private static JsonElement Json(string source)
    {
        using var document = JsonDocument.Parse(source);
        return document.RootElement.Clone();
    }

    private sealed class RatingValidator : IUserTaskFieldControlValidator
    {
        public string Id => ControlId;
        public string ValueKind => "number";

        public IReadOnlyList<string> ValidateDefinition(UserTaskField field) => field.Configuration is { } configuration &&
            configuration.ValueKind == JsonValueKind.Object && configuration.TryGetProperty("maximum", out var maximum) && maximum.TryGetInt32(out var value) && value > 0
                ? [] : ["A positive maximum rating is required."];

        public string? ValidateResponse(UserTaskField field, JsonElement value)
        {
            var maximum = field.Configuration!.Value.GetProperty("maximum").GetInt32();
            return value.TryGetInt32(out var rating) && rating is >= 1 && rating <= maximum ? null : "Rating must be between 1 and 5.";
        }
    }
}