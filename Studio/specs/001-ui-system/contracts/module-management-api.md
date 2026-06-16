# Contract: Module Management API

## Purpose

Module Management needs enough backend-facing data to present frontend and
backend modules in one registry. This contract describes the desired API surface;
implementation may extend the existing Studio modules endpoint or add a
dedicated endpoint if that keeps compatibility cleaner.

## Registry response

```json
{
  "hostVersion": "string",
  "sdkVersion": "string",
  "generatedAt": "2026-06-16T00:00:00Z",
  "modules": [
    {
      "id": "Elsa.Studio.FeatureManagement",
      "displayName": "Feature Management",
      "sourceKind": "backend",
      "scope": "full-stack",
      "version": "1.0.0",
      "sdkVersion": "1.0.0",
      "compatibility": "compatible",
      "status": "loaded",
      "manifest": {
        "path": "string",
        "hash": "string"
      },
      "contributions": {
        "navigation": [],
        "routes": [],
        "panels": [],
        "widgets": [],
        "settingEditors": [],
        "endpoints": []
      },
      "diagnostics": []
    }
  ]
}
```

## Status values

- `loaded`
- `available`
- `disabled`
- `failed`
- `incompatible`
- `pending`
- `unknown`

## Compatibility values

- `compatible`
- `warning`
- `incompatible`
- `unknown`

## Contribution shape

```json
{
  "type": "route",
  "id": "feature-management",
  "label": "Features",
  "path": "/features",
  "order": 130,
  "status": "active",
  "diagnostics": []
}
```

## Diagnostic shape

```json
{
  "level": "warning",
  "code": "SDK_VERSION_WARNING",
  "message": "Module was built against an older SDK version.",
  "timestamp": "2026-06-16T00:00:00Z",
  "source": "manifest"
}
```

## Compatibility notes

- The existing manifest endpoint remains valid for the current shell loader.
- Module Management may use a richer endpoint so UI diagnostics can evolve
  without breaking module loading.
- Backend-only modules that do not contribute frontend assets still appear in
  the registry when they expose Studio-relevant services, endpoints, or
  diagnostics.
