# Data Model: React Admin Module Host

## AdminModuleManifest

- `Id`: Stable module id.
- `DisplayName`: Human-readable module name.
- `Version`: Module version.
- `Entry`: Same-origin URL to the ESM entry.
- `Styles`: Same-origin stylesheet URLs.
- `RequiredHostVersion`: Compatible admin host version range.
- `RequiredSdkVersion`: Compatible admin SDK version range.
- `Capabilities`: Declared module capability tags.

## AdminModuleDiagnostic

- `ModuleId`: Module id or unknown module marker.
- `Status`: Availability/load status.
- `Reason`: Human-readable diagnostic reason.

## Browser Registry

Contains route, navigation, dashboard widget, panel, toolbar, editor, workflow-designer placeholder, and diagnostics contributions.
