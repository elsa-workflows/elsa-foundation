# Output binding coercion authoring and inspection

This note describes the implemented authoring and inspection surface for the
[Typed Output Binding Coercion PRD](../plans/output-binding-coercion-prd.md).
Terminology such as output binding coercion and conversion profile is defined in
the [Elsa glossary](../glossary/elsa.md).

## Draft and code-first controls

Authored activity inputs and reusable-activity output captures carry conversion
intent on `ArgumentState.Conversion`. A null conversion request is the legacy
default. Activity inputs use legacy/default `Auto`; output captures preserve the
legacy unplanned runtime shape unless an explicit conversion request is authored.
Use `{ "mode": "Auto" }` when an output capture should pin an explicit Auto plan.

Supported authored modes are:

- `Auto` for deterministic default behavior.
- `None` for explicit raw/identity behavior; publication rejects non-identical
  source and target contracts.
- `Json` for the built-in `elsa.json@1` profile.
- `Xml` for the built-in `elsa.xml@1` profile.
- `Profile` with `{ Id, Version }` for a registered named profile.

Code-first callers can set this through `ActivityArgument<T>`:

```csharp
inputs.Set(
    "payload",
    ActivityArgument.Value("""{"name":"Grace"}""")
        .WithConversion(ActivityArgument.Json()));

inputs.Set(
    "payload",
    ActivityArgument.Value("""{"name":"Grace"}""")
        .WithProfile("partner.customer-json", "3"));
```

Visual/API callers use the same JSON shape:

```json
{
  "referenceKey": "payload",
  "value": {
    "value": "{\"name\":\"Grace\"}",
    "expressionType": "Literal"
  },
  "conversion": {
    "mode": "Json"
  }
}
```

`IValueConversionProfileRegistry.List()` exposes the profiles available to visual
pickers. Hosts that only support lookup can keep implementing `TryGet`; the
default `List()` result is empty.

## Executable controls

The durable binding edge owns conversion policy. Publication resolves authored
intent to the compiled input binding or output-capture `ValueConversionPlan`:

- `Mode = Auto` for deterministic default behavior.
- `Mode = None` to require an exact source and target contract match.
- `Mode = Json` to require the built-in `elsa.json@1` profile.
- `Mode = Xml` to require the built-in `elsa.xml@1` profile.
- `Mode = Profile` with `Profile = { Id, Version }` for a registered named profile.

Publication resolves source representation, source type, target type, requested
mode/profile, limits, and options into the pinned plan stored on the executable.
Runtime applies only the pinned plan; it does not rediscover converters. Visual
Design draft controls compile to this same executable plan shape rather than
carrying conversion behavior in free-form metadata.

Direct activity-result inputs carry the authored request until the executable tree
is linked, because the producer output/projection contract is only known after all
nodes are compiled. The linker resolves that request to a pinned plan and clears
the temporary request. Workflow-request and variable-read bindings keep `Auto`
until their source contract is explicitly declared by a future authored surface.

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
inputs.Set(
    "payload",
    ActivityArgument.Value("""{"name":"Grace","tags":["dynamic"]}""")
        .WithConversion(ActivityArgument.Json()));
```

JSON formatted content to a registered typed alias:

```csharp
inputs.Set(
    "customer",
    ActivityArgument.Value("""{"name":"Grace"}""")
        .WithConversion(ActivityArgument.Json()));
```

XML formatted content to a registered typed alias:

```csharp
inputs.Set(
    "customer",
    ActivityArgument.Value("""<customer><name>Grace</name></customer>""")
        .WithConversion(ActivityArgument.Xml()));
```

Raw text preservation:

```csharp
inputs.Set(
    "body",
    ActivityArgument.Value("leave this text unchanged")
        .WithConversion(ActivityArgument.None()));
```

Explicit named profile:

```csharp
inputs.Set(
    "customer",
    ActivityArgument.Value("""{"name":"Grace"}""")
        .WithProfile("partner.customer-json", "3"));
```
