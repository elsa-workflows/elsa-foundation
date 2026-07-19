# Output binding coercion authoring and inspection

This note describes the implemented authoring and inspection surface for the
[Typed Output Binding Coercion PRD](../plans/output-binding-coercion-prd.md).
Terminology such as output binding coercion and conversion profile is defined in
the [Elsa glossary](../glossary/elsa.md).

## Executable controls

The durable binding edge owns conversion policy. Executable API and code-first
callers express that policy by setting the compiled input binding or output-capture
`ValueConversionPlan`:

- `Mode = Auto` for deterministic default behavior.
- `Mode = None` to require an exact source and target contract match.
- `Mode = Json` to require the built-in `elsa.json@1` profile.
- `Mode = Xml` to require the built-in `elsa.xml@1` profile.
- `Mode = Profile` with `Profile = { Id, Version }` for a registered named profile.

Publication resolves source representation, source type, target type, requested
mode/profile, limits, and options into the pinned plan stored on the executable.
Runtime applies only the pinned plan; it does not rediscover converters. Visual
Design draft controls should compile to this same executable plan shape rather
than carrying conversion behavior in free-form metadata.

## Inspection

`WorkflowExecutableInspector.GetInputSourcesAsync` exposes non-sensitive compiled
input bindings with their resolved `ConversionPlan`. `WorkflowExecutableInspector.GetAsync`
also exposes output captures with their resolved `ConversionPlan`. The plan includes:

- source representation;
- source and target contracts;
- selected mode;
- pinned profile id/version when a profile is used;
- limits and options;
- deterministic fingerprint.

Sensitive bindings redact the plan together with source payload details, because
profile/options metadata can reveal binding intent for protected values.

## Examples

JSON formatted content to dynamic `Any`:

```csharp
new ValueConversionPlan(
    ValueConversionPlan.CurrentSchemaVersion,
    ValueRepresentation.FormattedContent,
    new ValueTypeDescriptor("String"),
    new ValueTypeDescriptor("Elsa.Any"),
    ValueConversionMode.Json,
    ValueConversionOperation.Profile,
    new ValueConversionProfileReference("elsa.json", "1"),
    ValueConversionLimits.Default,
    options: null);
```

XML formatted content to a registered typed alias:

```csharp
new ValueConversionPlan(
    ValueConversionPlan.CurrentSchemaVersion,
    ValueRepresentation.FormattedContent,
    new ValueTypeDescriptor("String"),
    new ValueTypeDescriptor("Acme.Customer"),
    ValueConversionMode.Xml,
    ValueConversionOperation.Profile,
    new ValueConversionProfileReference("elsa.xml", "1"),
    ValueConversionLimits.Default,
    options: null);
```

Raw text preservation:

```csharp
ValueConversionPlan.Identity(
    new ValueTypeDescriptor("String"),
    ValueRepresentation.TextValue,
    ValueConversionMode.None);
```
