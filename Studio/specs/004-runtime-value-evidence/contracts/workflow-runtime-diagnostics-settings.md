# Contract: Workflow Runtime Diagnostics Settings

## Purpose

This contract describes the Studio-facing settings API for requesting runtime value evidence capture for the Workflows module. Studio stores a requested setting; the backend returns an effective setting after applying Host Policy caps and permission rules.

## Evidence Levels

Levels are ordered from least to most sensitive:

- `Off`: do not capture value evidence.
- `Metadata`: capture execution metadata only.
- `DiagnosticSnapshot`: capture bounded, sanitized Diagnostic Snapshots.
- `Payload`: capture full payload values. This requires explicit Host Policy approval and permissions.

## Evidence Subjects

Subject identifiers are stable API values:

- `workflowInputs`
- `workflowOutputs`
- `activityInputs`
- `activityOutputs`
- `incidents`
- `diagnostics`
- `containerVariables`
- `durableValues`

Unknown subjects are invalid.

## GET Runtime Diagnostics Settings

`GET /_elsa/workflow-management/runtime-diagnostics/settings`

Returns the requested setting, the effective setting, host-policy caps, and UI permission hints.

```json
{
  "requested": {
    "defaultLevel": "DiagnosticSnapshot",
    "subjectOverrides": {
      "durableValues": "Metadata"
    }
  },
  "effective": {
    "defaultLevel": "DiagnosticSnapshot",
    "subjectOverrides": {
      "durableValues": "Metadata"
    },
    "limitationReasons": []
  },
  "hostPolicy": {
    "maximumLevel": "DiagnosticSnapshot",
    "subjectMaximums": {
      "durableValues": "Metadata"
    },
    "limitationReasons": [
      "Full payload capture is disabled by host policy."
    ]
  },
  "permissions": {
    "canManage": true,
    "canEnableFullPayloads": false
  }
}
```

### Response Rules

- `requested` is the Studio-managed desired configuration.
- `effective` is what runtime capture must use for future capture events.
- `effective.defaultLevel` must never exceed `hostPolicy.maximumLevel`.
- `effective.subjectOverrides` must never exceed either the requested subject level or the matching host subject maximum.
- `limitationReasons` should be stable enough for UI display and diagnostics, but clients must not parse them for policy logic.

## PUT Runtime Diagnostics Settings

`PUT /_elsa/workflow-management/runtime-diagnostics/settings`

Requests a new Studio-managed runtime diagnostics setting.

```json
{
  "defaultLevel": "DiagnosticSnapshot",
  "subjectOverrides": {
    "activityInputs": "DiagnosticSnapshot",
    "activityOutputs": "DiagnosticSnapshot",
    "durableValues": "Metadata"
  }
}
```

The response shape is the same as `GET` and returns the resulting requested and effective settings.

### Write Rules

- The caller must have `workflows.runtimeDiagnostics.manage`.
- Requesting `Payload` requires `workflows.runtimeDiagnostics.enableFullPayloads` and Host Policy that allows `Payload`.
- Unknown levels, unknown subjects, and invalid values return `400 Bad Request`.
- A caller without manage permission receives `403 Forbidden`.
- A valid request that exceeds Host Policy may either be accepted and capped in `effective`, or rejected with `403 Forbidden`; the implementation must choose one behavior and apply it consistently. Studio must render the returned effective state.
- Settings changes apply only to future capture events. Existing workflow instance evidence is immutable.

## Permission Names

- `workflows.runtimeDiagnostics.manage`: allows changing requested runtime diagnostics settings.
- `workflows.runtimeDiagnostics.enableFullPayloads`: allows requesting Full Payload capture when Host Policy permits it.
- `workflows.runtimeEvidence.viewSnapshots`: allows viewing Diagnostic Snapshot evidence.
- `workflows.runtimeEvidence.viewPayloads`: allows viewing Full Payload evidence when captured.
- `workflows.runtimeEvidence.resolveReferences`: reserved for future reveal/download of Runtime Payload References.

## UI Rendering Requirements

- Show requested and effective levels together when they differ.
- Show host-policy limitation reasons close to the affected setting.
- Disable or hide controls the current user cannot change.
- Explain that changes affect future runs only.
- Avoid implying that a lower effective level can be bypassed from Studio.
