using System.Globalization;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Secrets.Core.Models;

namespace Elsa.Jobs.Secrets;

internal sealed class JobSecretCatalog
{
    private readonly IReadOnlyList<JobSecretDeclaration> _declarations;
    internal JobSecretCatalog(IEnumerable<JobSecretDeclaration> declarations)
    {
        try
        {
            var entries = declarations.ToArray();
            if (entries.Length > 256) throw new ArgumentException();
            foreach (var entry in entries)
            {
                Name(entry.Handler.JobType); Name(entry.Handler.Version); Name(entry.InputSchema.Alias);
                if (entry.InputSchema.Version < 1 || entry.ReferenceSlots.Count > 32 || entry.ProtectedInputPointers.Count > 32) throw new ArgumentException();
                var pointers = new HashSet<string>(StringComparer.Ordinal);
                foreach (var slot in entry.ReferenceSlots)
                {
                    _ = Segments(slot.Pointer);
                    if (!pointers.Add(slot.Pointer) || slot.AllowedReferences.Count is < 1 or > 32) throw new ArgumentException();
                    if (slot.AllowedReferences.Select(Normalize).Distinct().Count() != slot.AllowedReferences.Count) throw new ArgumentException();
                }
                foreach (var pointer in entry.ProtectedInputPointers) { _ = Segments(pointer); if (!pointers.Add(pointer)) throw new ArgumentException(); }
            }
            if (entries.GroupBy(entry => (entry.Handler, entry.InputSchema)).Any(group => group.Count() != 1)) throw new ArgumentException();
            _declarations = Array.AsReadOnly(entries.Select(entry => new JobSecretDeclaration(entry.Handler, entry.InputSchema,
                entry.ReferenceSlots.Select(slot => new JobSecretReferenceSlot(slot.Pointer, slot.AllowedReferences.Select(Normalize).ToArray(), slot.Required)).ToArray(), entry.ProtectedInputPointers)).ToArray());
        }
        catch (Exception error) when (error is ArgumentException or NullReferenceException)
        { throw new ArgumentException("The Jobs Secrets declaration is invalid."); }
    }

    internal JobSecretInspection Inspect(JobScope scope, JobRequest request)
    {
        if (scope is null || request?.Handler is null || request.Input?.Schema is null) throw new JobDataPolicyException();
        var declaration = _declarations.SingleOrDefault(entry => entry.Handler == request.Handler && entry.InputSchema == request.Input.Schema);
        if (declaration is null) return new(null, [], []);
        try
        {
            if (JobPayloadLimits.GetByteCount(request.Input) > JobPayloadLimits.MaximumBytes) throw new JobDataPolicyException();
            var declared = declaration.ReferenceSlots.Select(slot => slot.Pointer).ToHashSet(StringComparer.Ordinal);
            Walk(request.Input.Value, "", declared, 0);
            var references = new List<SecretReference>(); var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in declaration.ReferenceSlots)
            {
                if (!TryRead(request.Input.Value, slot.Pointer, out var value) || value.ValueKind == JsonValueKind.Null)
                { if (slot.Required) throw new JobDataPolicyException(); continue; }
                var reference = Parse(value);
                if (!slot.AllowedReferences.Contains(reference)) throw new JobDataPolicyException();
                references.Add(reference);
            }
            if (references.Count > 0) Name(scope.TenantId!);
            foreach (var pointer in declaration.ProtectedInputPointers)
            {
                if (!TryRead(request.Input.Value, pointer, out var value)) throw new JobDataPolicyException();
                foreach (var literal in Scalar(value))
                {
                    if (literal.Length > 65536) throw new JobDataPolicyException();
                    if (literal.Length > 0) values.Add(literal);
                }
            }
            return new(declaration, references.Distinct().ToArray(), values.ToArray());
        }
        catch (JobDataPolicyException) { throw; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or FormatException or OverflowException or JobException)
        { throw new JobDataPolicyException(); }
    }

    internal static SecretReference Normalize(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Name is null || reference.Name.Length > SecretNameConstraints.MaximumLength) throw new ArgumentException();
        var name = reference.Name.Trim(); SecretNameConstraints.Validate(name);
        Optional(reference.TypeName); Optional(reference.Scope);
        return new(name.ToLowerInvariant(), reference.TypeName?.ToLowerInvariant(), reference.Scope?.ToLowerInvariant());
    }
    private static SecretReference Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JobDataPolicyException();
        var members = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in value.EnumerateObject())
        {
            if (!AllowedMember(member.Name) || !members.TryAdd(member.Name, member.Value)) throw new JobDataPolicyException();
        }
        if (!members.TryGetValue("name", out var name) || name.ValueKind != JsonValueKind.String) throw new JobDataPolicyException();
        string? OptionalMember(string key) => !members.TryGetValue(key, out var field) || field.ValueKind == JsonValueKind.Null ? null
            : field.ValueKind == JsonValueKind.String ? field.GetString() : throw new JobDataPolicyException();
        return Normalize(new(name.GetString()!, OptionalMember("typeName"), OptionalMember("scope")));
    }
    private static bool AllowedMember(string name) => name.Equals("name", StringComparison.OrdinalIgnoreCase) || name.Equals("typeName", StringComparison.OrdinalIgnoreCase) || name.Equals("scope", StringComparison.OrdinalIgnoreCase);
    private static void Walk(JsonElement value, string pointer, IReadOnlySet<string> declared, int depth)
    {
        if (depth > 32) throw new JobDataPolicyException();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length) throw new JobDataPolicyException();
            if (properties.Any(item => item.Name.Equals("name", StringComparison.OrdinalIgnoreCase)) && properties.All(item => AllowedMember(item.Name)) && !declared.Contains(pointer)) throw new JobDataPolicyException();
            foreach (var property in properties) Walk(property.Value, pointer + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"), declared, depth + 1);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) Walk(item, pointer + "/" + (index++).ToString(CultureInfo.InvariantCulture), declared, depth + 1);
        }
    }
    private static string[] Segments(string pointer)
    {
        if (pointer is null || pointer.Length > 1024 || pointer.Length > 0 && pointer[0] != '/') throw new ArgumentException();
        if (pointer.Length == 0) return [];
        var result = pointer[1..].Split('/'); if (result.Length > 32) throw new ArgumentException();
        for (var index = 0; index < result.Length; index++)
        {
            var text = result[index];
            for (var offset = 0; offset < text.Length; offset++) if (text[offset] == '~' && (offset + 1 == text.Length || text[++offset] is not ('0' or '1'))) throw new ArgumentException();
            result[index] = text.Replace("~1", "/").Replace("~0", "~");
        }
        return result;
    }
    private static bool TryRead(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;
        foreach (var segment in Segments(pointer))
        {
            if (value.ValueKind == JsonValueKind.Object) { if (!value.TryGetProperty(segment, out value)) return false; }
            else if (value.ValueKind == JsonValueKind.Array && (segment.Length == 1 || !segment.StartsWith('0')) && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < value.GetArrayLength()) value = value[index];
            else return false;
        }
        return true;
    }
    internal static IEnumerable<string> Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => [], JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Number when value.TryGetDecimal(out var number) => [value.GetRawText(), number.ToString("G29", CultureInfo.InvariantCulture)],
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [value.GetRawText()],
        _ => throw new JobDataPolicyException()
    };
    private static void Name(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException(); }
    private static void Optional(string? value) { if (value is not null) Name(value); }
}

internal sealed record JobSecretInspection(JobSecretDeclaration? Declaration, IReadOnlyList<SecretReference> References, IReadOnlyList<string> ProtectedValues);
