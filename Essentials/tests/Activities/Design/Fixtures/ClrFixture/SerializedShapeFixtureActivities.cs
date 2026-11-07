using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.ClrFixture;

/// <summary>A typed whole result with deliberately no named Output declarations.</summary>
public sealed class SerializedCustomerFixtureActivity : Activity<SerializedCustomer>
{
    protected override ValueTask<ActivityTransition<SerializedCustomer>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new SerializedCustomer()));
}

public sealed class SerializedCustomer
{
    [JsonPropertyName("customer.id")]
    public Guid CustomerId { get; set; }

    [JsonPropertyName("")]
    public string EmptyName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Hidden { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SerializedAddress? OptionalAddress { get; set; }

    public SerializedAddress? NullableAddress { get; set; }

    [AllowNull]
    public string NonNullableRead { get; set; } = string.Empty;

    [MaybeNull]
    public string NullableRead { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DefaultCount { get; set; }

    public List<SerializedAddress> Addresses { get; set; } = [];
    public Dictionary<string, SerializedAddress?> AddressByKey { get; set; } = [];
    public HashSet<string> Tags { get; set; } = [];
    public string PublicField = "not serialized";
}

public sealed class SerializedAddress
{
    public string City { get; set; } = string.Empty;
    public int ZipCode { get; set; }
}

public sealed class SerializedRecursive
{
    public SerializedRecursive? Child { get; set; }
    public SerializedAddress First { get; set; } = new();
    public SerializedAddress Second { get; set; } = new();
}

[MetadataSideEffect]
public sealed class MetadataOnlyPayload
{
    public MetadataOnlyPayload() => throw new InvalidOperationException("Discovery must not construct a DTO.");
    [MetadataSideEffect]
    public string ThrowsOnRead => throw new InvalidOperationException("Discovery must not invoke a getter.");
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class MetadataSideEffectAttribute : Attribute
{
    public MetadataSideEffectAttribute() => throw new InvalidOperationException("Discovery must not construct an attribute.");
}

[JsonConverter(typeof(ExplodingPayloadConverter))]
public sealed class ConverterPayload;

public enum PlainNullableEnum { One }

[JsonConverter(typeof(ExplodingEnumConverter))]
public enum ConvertedEnum { One }

public sealed class NullableEnumHolder
{
    public PlainNullableEnum? Plain { get; set; }
    public ConvertedEnum? Converted { get; set; }
}

public sealed class ExplodingEnumConverter : JsonConverter<ConvertedEnum>
{
    public ExplodingEnumConverter() => throw new InvalidOperationException("Discovery must not construct a converter.");
    public override ConvertedEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, ConvertedEnum value, JsonSerializerOptions options) => throw new NotSupportedException();
}

public sealed class PropertyConverterPayload
{
    [JsonConverter(typeof(ExplodingPayloadConverter))]
    public ConverterPayload? Value { get; set; }
}

public sealed class ExplodingPayloadConverter : JsonConverter<ConverterPayload>
{
    public ExplodingPayloadConverter() => throw new InvalidOperationException("Discovery must not construct a converter.");
    public override ConverterPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, ConverterPayload value, JsonSerializerOptions options) => throw new NotSupportedException();
}

public sealed class ExtensionDataPayload
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class IncludedFieldPayload
{
    [JsonInclude]
    public string Field = string.Empty;
}

[JsonPolymorphic]
[JsonDerivedType(typeof(DerivedPayload), "derived")]
public class PolymorphicPayload;
public sealed class DerivedPayload : PolymorphicPayload;

public class SerializedOverrideBase
{
    [JsonIgnore]
    public virtual string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual string? Optional { get; set; }
    public virtual string? Removed { get; set; }
    [JsonIgnore]
    public string? Hidden { get; set; }
}

public sealed class SerializedOverridePayload : SerializedOverrideBase
{
    public override string? Name { get; set; }
    public override string? Optional { get; set; }
    [JsonIgnore]
    public override string? Removed { get; set; }
}

public class SerializedRenamedBase
{
    [JsonPropertyName("base_name")]
    public virtual string Name { get; set; } = "";
}
public sealed class SerializedRenamedOverride : SerializedRenamedBase
{
    public override string Name { get; set; } = "";
}
public sealed class SerializedRenamedShadow : SerializedRenamedBase
{
    public new string Name { get; set; } = "";
}
public sealed class SerializedIgnoredShadow : SerializedRenamedBase
{
    [JsonIgnore]
    public new string Name { get; set; } = "";
}
