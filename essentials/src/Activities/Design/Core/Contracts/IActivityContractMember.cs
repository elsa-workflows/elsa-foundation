using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>What every public contract member carries, whether it is an input, an output or an outcome.</summary>
public interface IActivityContractMember
{
    /// <summary>Stable identity used for binding and diffing.</summary>
    string ReferenceKey { get; }

    string Name { get; }

    string? Description { get; }
}

/// <summary>
/// What inputs and outputs share as value-carrying contract members. <c>IsRequired</c> is deliberately absent:
/// on an input it means the caller must bind a value unless a default applies, on an output it means the
/// implementation must produce one, so compatibility rules for it run in opposite directions.
/// </summary>
public interface IActivityContractValueMember : IActivityContractMember
{
    TypeReference Type { get; }

    bool IsNullable { get; }

    string StorageDriverKey { get; }

    ActivityBoundaryDurability Durability { get; }

    string? DisplayName { get; }

    string? Category { get; }

    float Order { get; }

    string? UiHint { get; }

    JsonElement? UiSpecifications { get; }
}
