namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>
/// A peek at the live <c>IActivity</c> produced by crossing the construction seam. It deliberately
/// mixes both sides of the bridge so the crossing is visible in one payload:
/// <list type="bullet">
///   <item><see cref="DescriptorType"/> + <see cref="Inputs"/>/<see cref="Outputs"/> come from the
///   <b>Design</b> seam (the persisted catalog row's descriptor + argument definitions).</item>
///   <item><see cref="RuntimeType"/>, <see cref="Properties"/>, <see cref="SyntheticProperties"/> and
///   <see cref="CustomProperties"/> come from the <b>Runtime</b> seam (the constructed object).</item>
/// </list>
/// </summary>
public sealed record ConstructedActivityView(
    string ActivityVersionId,
    string DescriptorType,
    string RuntimeType,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyDictionary<string, object> SyntheticProperties,
    IReadOnlyDictionary<string, object> CustomProperties,
    IReadOnlyList<ArgumentView> Inputs,
    IReadOnlyList<ArgumentView> Outputs
);

/// <summary>A design-time argument definition, projected to its bare bones for the demo view.</summary>
public sealed record ArgumentView(string ReferenceKey, string Name, string TypeName);
