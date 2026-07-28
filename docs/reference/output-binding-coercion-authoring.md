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

### Profile picker endpoint

`GET publishing/value-conversion/profiles` projects the active shell registry for
authoring pickers. It is gated by the `workflow-publishing.read` permission of its
owning publishing domain and advertised
in the API capability document under the `elsa.api.expressions` capability as
relation `conversion-profiles`; clients that predate the relation fall back
gracefully. The default host returns the built-in `elsa.json@1` and `elsa.xml@1`
profiles, and a host-registered `IValueConversionProfileRegistry` surfaces its own
profiles. Each item carries the versioned profile identity plus the capabilities
publication resolves against, so a picker cannot pin a profile the executable would
reject:

```json
{
  "items": [
    {
      "profile": { "id": "elsa.json", "version": "1" },
      "supportedSourceRepresentations": ["StructuredValue", "FormattedContent"],
      "supportedTargetAliases": ["*"]
    }
  ]
}
```

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

## Publish and preflight conversion diagnostics

When a binding cannot be resolved to a deterministic conversion plan, the
`POST publishing/workflows/{versionId}/publish` and
`POST publishing/workflows/{versionId}/preflight` endpoints reject the request with
HTTP `400` and an `application/problem+json` body (RFC 7807). Publish and preflight
return the identical payload. The failing node id, binding reference key, source and
target contracts, source representation, requested mode, pinned profile, and a stable
machine-readable `reasonCode` are carried as structured fields, so clients never need to
parse the human message.

The problem `type` is `https://elsa.dev/problems/VF-COER-001`, the `errorCode` and the
single diagnostic `code` are both `VF-COER-001`, and the original human message is kept in
both `detail` and the diagnostic `message`. The diagnostic `subject.id` is the failing node
id, `location.referenceKey` is the input/output reference key, and `metadata` carries the
contracts, representation, mode, profile, and reason code:

```json
{
  "type": "https://elsa.dev/problems/VF-COER-001",
  "title": "Workflow publication conversion was rejected",
  "status": 400,
  "detail": "VF-COER-001: Cannot resolve conversion from source representation 'TypedValue' and contract 'Int64/Single/schema:none' to target contract 'Int32/Single/schema:none' using mode 'Auto': numeric narrowing or cross-family numeric conversion is lossy under Auto.",
  "errorCode": "VF-COER-001",
  "traceId": "0HN...",
  "diagnostics": [
    {
      "code": "VF-COER-001",
      "severity": "Error",
      "message": "VF-COER-001: Cannot resolve conversion from source representation 'TypedValue' and contract 'Int64/Single/schema:none' to target contract 'Int32/Single/schema:none' using mode 'Auto': numeric narrowing or cross-family numeric conversion is lossy under Auto.",
      "subject": { "kind": "ActivityResult", "id": "consumer", "versionId": "workflow-version-1" },
      "location": { "referenceKey": "value" },
      "remediation": "numeric narrowing or cross-family numeric conversion is lossy under Auto.",
      "metadata": {
        "reasonCode": "AutomaticNumericLossy",
        "mode": "Auto",
        "targetType": "Int32/Single/schema:none",
        "sourceType": "Int64/Single/schema:none",
        "sourceRepresentation": "TypedValue",
        "nodeId": "consumer",
        "referenceKey": "value",
        "bindingKind": "ActivityResult",
        "workflowVersionId": "workflow-version-1"
      }
    }
  ]
}
```

`reasonCode` is stable across releases (for example `AutomaticNumericLossy`,
`NoneModeContractMismatch`, `ProfileNotAvailable`, `ProducerNodeMissing`). `profileId` and
`profileVersion` appear in `metadata` only when a profile is requested; `sourceType` and
`sourceRepresentation` are omitted when the producer source contract could not be resolved
(for example an activity-result binding whose producer node is missing). Non-conversion
publication failures are unchanged and still return a plain `400`.
