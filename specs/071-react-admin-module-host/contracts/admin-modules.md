# Contract: Admin Modules

## Manifest Endpoint

`GET /_elsa/admin/modules`

Response:

```json
{
  "hostVersion": "1.0.0",
  "sdkVersion": "1.0.0",
  "modules": [
    {
      "id": "Elsa.Admin.Samples.Dashboard",
      "displayName": "Dashboard Sample",
      "version": "1.0.0",
      "entry": "/_content/Elsa.Admin.Samples.Dashboard/admin/modules/dashboard/module.js",
      "styles": ["/_content/Elsa.Admin.Samples.Dashboard/admin/modules/dashboard/module.css"],
      "requiredHostVersion": "^1.0.0",
      "requiredSdkVersion": "^1.0.0",
      "capabilities": ["dashboard"]
    }
  ],
  "diagnostics": []
}
```

## Frontend Module Entry

```ts
import type { ElsaAdminModuleApi } from "@elsa-workflows/admin-sdk";

export function register(api: ElsaAdminModuleApi): void;
```

## Weather Sample Endpoint

`GET /_elsa/samples/weather-forecast`

Returns deterministic forecast rows for proving a server-backed admin module.
