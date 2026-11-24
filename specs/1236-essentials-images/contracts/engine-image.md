# Contract: the essentials images

## `elsaworkflows/elsa-server-essentials`

| Path | Mount | Purpose |
|---|---|---|
| `/app/config/appsettings.json` | file, read-only | The user's settings (overrides and added features). |
| `/app/packages` | folder | Nuplane directory feed for the user's feature packages. |
| `/app/workflows` | folder, read-only | Elsa 4 workflow definitions, imported and published at startup. Exists and is empty in the image. |
| `/app/data` | volume | SQLite databases and the Data Protection key ring. |

- Port 8080; the root path returns the health JSON, as the Workbench image does.
- Environment: `Production`. Environment variables override the user file.
- Tags: `latest`, `4.0.0-preview.<run>`, `sha-<short>`, as the Workbench image.

### Required settings

The engine refuses to start, naming the setting, when one of these is empty:

| Setting | Purpose |
|---|---|
| `CShells:Shells:default:Features:FoundationIdentityAspNetCoreIdentityEntityFrameworkCore:SeedAdminPassword` | Password of the seeded `admin` account. |
| `CShells:Shells:default:Features:FoundationIdentityOpenIddict:SigningKey` | Base64 PKCS#8 RSA key that signs access tokens. |
| `CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey` | HMAC key, at least 32 bytes. |
| `CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey` | HMAC key, at least 32 bytes. |
| `CShells:Shells:default:Features:WorkflowsRuntimeApi:WorkflowAlterationPayloadProtectionKeys` and `...ActiveKeyId` | Protects workflow alteration payloads. |
| `Elsa:ModuleManagement:ApiKey` | Shared with the Studio image's management bridge. |

The final list is whatever the composed features require; the implementation checks it against the refusals and
updates this table and `appsettings.example.json` together.

### Studio settings

Not required to start; set them when Studio is used. `Cors:AllowedOrigins` and the identity feature's
`AllowedReturnUrlOrigins` hold the Studio origin. The essentials list bakes no origins, so a user's list starts at
index 0.

### Extending the image (`FROM`)

| Path | Owner | Purpose |
|---|---|---|
| `/app/composition/essentials.features.json` | engine image | The essentials list. A derived image does not change it. |
| `/app/composition/<name>.features.json` | derived image | Its added baked features. The essentials list is read first, then the other files in ordinal file-name order. |
| `/app/composition/packages/` | derived image | Packages of its added features, loaded by Nuplane like `/app/packages`. |

A derived image uses the same version tag as its base. Users configure it exactly as the engine.

## `elsaworkflows/elsa-studio-essentials`

| Setting | Purpose |
|---|---|
| `Studio:BackendBaseUrl` | The engine's URL as the browser reaches it. |
| `Studio:BackendModuleManagementApiKey` | Same value as the engine's `Elsa:ModuleManagement:ApiKey`. |

Configured through `/app/config/appsettings.json` or environment variables. Its composition is baked; there is no
mounted `shells.json`.
