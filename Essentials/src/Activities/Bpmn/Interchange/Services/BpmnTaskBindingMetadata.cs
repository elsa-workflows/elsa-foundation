using System.Text;
using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using LibInterchange = global::Bpmn.Interchange;
using LibModel = global::Bpmn.Model;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>
/// The <c>elsa:taskBinding</c> vendor attribute: a task's exact activity version and its literal inputs, carried through
/// shared XML only when every input is portable. Export decides what may travel; import never executes what it reads
/// until the author confirms it.
/// </summary>
internal static class BpmnTaskBindingMetadata
{
    private const string AttributeName = "taskBinding";
    private const int MaximumBytes = 16 * 1024;
    private const int MaximumDepth = 32;
    private const int MaximumIdLength = 512;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The BPMN task types whose bound child is the author's own work; every other element's child is re-established by import.</summary>
    public static readonly IReadOnlySet<string> TaskTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        BpmnElementTypes.Task, BpmnElementTypes.UserTask, BpmnElementTypes.ServiceTask,
        BpmnElementTypes.ScriptTask, BpmnElementTypes.ManualTask, BpmnElementTypes.BusinessRuleTask
    };

    /// <summary>Refuses an element carrying the attribute under both vendor namespace aliases, before translation could keep only one.</summary>
    public static void ValidateSourceAttributes(System.Xml.Linq.XDocument document)
    {
        foreach (var element in document.Descendants())
            if (element.Attributes().Count(attribute => attribute.Name.LocalName == AttributeName &&
                    (attribute.Name.NamespaceName == BpmnVendorNamespace.NamespaceName ||
                     attribute.Name.NamespaceName == LibInterchange.BpmnXmlNames.VendorNamespaceName)) > 1)
                throw new Exceptions.BpmnInterchangeException($"Element '{element.Attribute("id")?.Value ?? element.Name.LocalName}' has ambiguous task binding attributes.");
    }

    public static bool IsAttribute(LibModel.BpmnForeignAttribute attribute) =>
        attribute.Name.Namespace == LibInterchange.BpmnXmlNames.VendorNamespaceName &&
        attribute.Name.LocalName == AttributeName;

    public static LibModel.BpmnForeignAttribute ToAttribute(string activityVersionId, IReadOnlyList<ArgumentState> inputs) =>
        new(new LibModel.BpmnQName(LibInterchange.BpmnXmlNames.VendorNamespaceName, AttributeName), JsonSerializer.Serialize(new Carried(
            activityVersionId,
            inputs.ToDictionary(input => input.ReferenceKey, input => JsonSerializer.SerializeToElement(input.Value.Value), StringComparer.Ordinal)), Web));

    /// <summary>Whether a catalog input declaration may travel with a literal value: never a sensitive, credential, masked or connection input.</summary>
    public static bool IsPortable(InputDefinition declaration) =>
        declaration.IsSensitive != true &&
        declaration.IsCredential != true &&
        declaration.ConnectionType is null &&
        !StringComparer.OrdinalIgnoreCase.Equals(declaration.UiHint, "password");

    public static bool IsLiteral(ArgumentValue value) => StringComparer.OrdinalIgnoreCase.Equals(value.ExpressionType, "Literal");

    /// <summary>Reads a retained binding, or says why it cannot be read; a binding that cannot be read is never proposed.</summary>
    public static bool TryRead(string? value, out string activityVersionId, out IReadOnlyList<ArgumentState> inputs, out string? problem)
    {
        activityVersionId = "";
        inputs = [];
        problem = null;
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaximumBytes)
        {
            problem = $"The retained task binding must be nonblank JSON of at most {MaximumBytes} UTF-8 bytes.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && HasDuplicateNames(root))
            {
                problem = "The retained task binding names a member more than once.";
                return false;
            }
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("activityVersionId", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > MaximumIdLength ||
                root.TryGetProperty("inputs", out var carried) && carried.ValueKind != JsonValueKind.Object)
            {
                problem = "The retained task binding must name an activity version and carry its inputs as an object.";
                return false;
            }
            if (root.TryGetProperty("inputs", out var keyed) && HasDuplicateNames(keyed))
            {
                problem = "The retained task binding names an input more than once.";
                return false;
            }
            activityVersionId = id.GetString()!;
            inputs = root.TryGetProperty("inputs", out var values)
                ? values.EnumerateObject()
                    .Where(input => !string.IsNullOrWhiteSpace(input.Name))
                    .Select(input => new ArgumentState(input.Name, new ArgumentValue(input.Value.Clone(), "Literal"), null, null, null, null))
                    .ToArray()
                : [];
            return true;
        }
        catch (JsonException)
        {
            problem = "The retained task binding JSON is invalid.";
            return false;
        }
    }

    private static bool HasDuplicateNames(JsonElement value) =>
        value.EnumerateObject().GroupBy(member => member.Name, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private sealed record Carried(string ActivityVersionId, IReadOnlyDictionary<string, JsonElement> Inputs);
}
