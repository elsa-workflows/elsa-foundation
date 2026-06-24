# Studio Contract: Secrets Module

Foundation Studio consumes the backend API and contributes a Secrets feature area plus a property editor contribution.

## Feature Area

Feature area:

- ID: `security`
- Navigation group: `Administration`
- Owned paths: `/security`
- Secrets route: `/security/secrets`
- Detail route: `/security/secrets?secret={name}` or equivalent deep-linkable state

The module should report a clear unavailable state if backend descriptors or list endpoints return not found or unavailable.

## Management Views

Secrets list:

- Search input.
- Type, store, scope, and status filters.
- Table or dense list with name, display name, type, store, scope, status, current version, updated timestamp.
- Row actions: open, rotate, test, revoke, delete.
- Create command.

Secret detail:

- Safe metadata panel.
- Lifecycle/status panel.
- Metadata edit form.
- Rotation form.
- Test/revoke/delete commands.
- No current-value reveal.

Create dialog:

- Technical name.
- Display name.
- Description.
- Type.
- Store.
- Scope.
- Tags.
- Value field for direct-value stores.
- Configuration key field for configuration-backed references.
- Expiration.

## Secret Picker Contribution

Property editor contribution:

- ID: `studio.property.secret-picker`
- Supports inputs with `uiHint: "secret-picker"` or a future sensitive-input marker exposed by activity descriptors.
- Reads picker options from descriptor `uiSpecifications` when present.
- Loads descriptors and compatible secrets.
- Lets the user select an existing secret.
- Offers inline creation when allowed.
- Writes a wrapped expression using syntax `Secret` and a `SecretReference` value.

Serialized value shape:

```json
{
  "typeName": "String",
  "expression": {
    "type": "Secret",
    "value": {
      "name": "smtp-password",
      "typeName": "text",
      "scope": "email"
    }
  }
}
```

Rules:

- The picker never stores the resolved value.
- Inline create must clear submitted value state after success.
- Disabled/read-only property contexts must not allow changes.
- If the backend feature is unavailable, the editor falls back to a disabled explanatory state rather than a literal secret field.

## Module Manifest

Capabilities:

- `feature-areas`
- `routes`
- `http`
- `property-editors`
- `security`

.NET registration tests must prove the module manifest is contributed when the Studio shell feature is enabled.
