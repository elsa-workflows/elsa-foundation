# User task form lifecycle

Studio manages a form definition rather than asking users to navigate separate version records. Each definition has at most one shared draft and zero or more immutable published versions. The existing `GET /runtime/user-task-forms` collection remains published-only for task-form pickers and integrations.

## Lifecycle operations

- `GET /runtime/user-task-forms/definitions` lists one row per tenant-scoped form definition, with `latestPublished`, an active `draft` when present, and the monotonic definition `revision`.
- `GET /runtime/user-task-forms/{id}/definition` opens that form. Reading a published form does not create or persist a draft; Studio starts from its active draft when one exists, otherwise from its latest published version.
- `GET /runtime/user-task-forms/{id}/versions` pages the immutable publication history for that form.
- `POST /runtime/user-task-forms/{id}/draft` accepts `{ "revision": n, "form": ... }`. The first save creates the shared draft and later saves replace it while advancing the revision. Incomplete definitions can be saved within structural limits; provider validation is authoritative at publication.
- `POST /runtime/user-task-forms/{id}/discard` accepts `{ "revision": n }`, removes the active draft, and advances the revision even when no publication exists. The revision tombstone prevents an old editor from recreating discarded content.
- `POST /runtime/user-task-forms/{id}/publish` accepts `{ "revision": n, "form": ... }`. It validates and publishes the submitted content as the next system-assigned version, atomically with clearing the draft and advancing the definition revision. Publishing submitted content includes edits that have not separately been saved as a draft.

All lifecycle mutations are tenant-scoped and conditional on the supplied revision. A stale save, discard, or publication returns a conflict rather than overwriting another editor's work. Form IDs are fixed once a definition is persisted; version numbers are assigned at publication. Discarding a draft-only form removes it from the definition list while retaining its revision tombstone internally.

## Studio autosave

Studio enables draft autosave by default and waits briefly after edits before sending the latest form snapshot. Saves are serialized against the definition revision, so edits made during an in-flight save remain in the editor and are saved afterward. Opening a published form does not create a draft; the first changed snapshot creates one. Draft saving allows incomplete forms within structural limits, while publication remains explicit and performs provider validation.

The toolbar shows pending, saving, saved, and failed states, and retains the manual `Save draft` action for immediate saves and retry after an error or conflict. An error pauses automatic retries to avoid a request loop; reload the latest draft or explicitly retry after resolving the problem. Unapplied JSON source edits are never autosaved. Turning autosave off leaves changes local until manually saved, and the editor guards browser/host navigation while local changes or saves remain. Back/cancel flushes pending autosave changes before leaving when possible.

## Field presentation and value semantics

Published form definitions may include an optional `presentation` object on Boolean and single-choice fields. Boolean fields support `checkbox`, `radio`, and `dropdown`; single-choice fields support `radio` and `dropdown`. Missing presentation metadata keeps the existing dropdown display for both field kinds, so older published forms retain their appearance. Boolean answer labels (`trueLabel`, `falseLabel`, and `unansweredLabel`) are presentation-only; the workflow response remains a JSON Boolean, and single-choice responses remain the selected choice string.

Optional Boolean fields distinguish an unanswered value from `false`: checkbox and radio displays provide a clear-answer action, and dropdowns include an unanswered option. A required Boolean may be answered `false`; `required` means an answer must be present, not that it must be `true`. The separate `mustBeTrue` constraint is supported only on Boolean fields and requires `true` for actions that require form answers. Optional form actions may leave that field unanswered or answer `false`.

Presentation metadata is stored with the versioned form definition and validated by the Basic Forms provider at publication and response time. Unsupported display/type combinations and Boolean answer labels on choice fields are rejected; changing a field's kind in Studio clears presentation and Boolean-only validation settings that no longer apply.

## Extending field controls

Modules add controls through the host-owned `userTaskFieldControls` Slot on `ElsaStudioModuleApi`. The contribution identity is stable and major-versioned (for example `acme.rating/1`); `valueKind` describes the response JSON type and a control's renderer is reused by the designer canvas, Preview, and task recipient form. `createField` supplies the default configuration, `propertiesEditor` edits control-specific settings, and `validateConfiguration` validates the JSON object stored under the field's `configuration` property. Forms owns the common key, label, help, required, and read-only properties.

For contributed scalar renderers, put `data-studio-field-control=""` on the actual native input or textarea, and forward the supplied `id`, `disabled`, and `aria-invalid` properties. This lets the shared `StudioField` own the single outline and full-width control layout; see [Outlined form fields](module-ui.md#outlined-form-fields).

```tsx
api.userTaskFieldControls?.add({
  id: "acme.rating/1",
  label: "Rating",
  valueKind: "number",
  createField: fields => ({
    key: `rating${fields.length + 1}`,
    label: "Rating",
    kind: "number",
    required: false,
    readOnly: false,
    configuration: { maximum: 5 }
  }),
  component: RatingControl,
  propertiesEditor: RatingProperties,
  validateConfiguration: value => isValidRatingConfiguration(value) ? [] : ["A positive maximum is required."]
});
```

Persisted custom fields include `controlId` and a bounded JSON-object `configuration`. Do not reuse a control identity for an incompatible contract; publish a new major identity instead. A missing, duplicate, or host-blocked control is shown as unavailable: Studio preserves its source and draft data but prevents publication and task completion rather than substituting another renderer. Hosts must also register an `IUserTaskFieldControlValidator` on the server for each control identity:

```csharp
services.AddScoped<IUserTaskFieldControlValidator, RatingValidator>();

public sealed class RatingValidator : IUserTaskFieldControlValidator
{
    public string Id => "acme.rating/1";
    public string ValueKind => "number";
    public IReadOnlyList<string> ValidateDefinition(UserTaskField field) => /* validate field.Configuration */ [];
    public string? ValidateResponse(UserTaskField field, JsonElement value) => /* validate response and configuration */ null;
}
```

Its `ValueKind`, `ValidateDefinition`, and `ValidateResponse` methods are authoritative; client-side contribution validation is a usability aid, not a security or workflow-data boundary. Drafts may be saved while a control module is absent, subject to the common structural and JSON size/depth limits.

Published versions remain immutable. Existing task records and workflow references continue to point to the version they already selected; publishing or editing a form does not rewrite them. Drafts are never returned by the published-form collection.