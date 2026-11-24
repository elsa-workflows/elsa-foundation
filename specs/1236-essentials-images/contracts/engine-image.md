# Contract: the essentials images

## `elsaworkflows/elsa-server-essentials`

| Path | Mount | Purpose |
|---|---|---|
| `/app/config/appsettings.json` | file, read-only | The user's settings (overrides and added features). |
| `/app/packages` | folder | Nuplane directory feed for the user's feature packages. |
| `/app/migrated` | folder, read-only | The migration CLI's output: `workflows/` (Elsa 4 workflow definitions, imported and published at startup) and `activities/activities.json` (composite activities, imported at startup). The image holds an empty `workflows/` and an `activities.json` of `[]`. |
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
| `CShells:Shells:default:Features:WorkflowsRuntimeApi:WorkflowAlterationPayloadProtectionKeys` and `WorkflowAlterationPayloadProtectionActiveKeyId` | Protects workflow alteration payloads. |

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
| `/app/composition/<name>.features.json` | derived image | Its added list, read by the composition library after the essentials list, in ordinal file-name order. |
| `/app/Elsa.Essentials.Composition.dll` and the listed features' assemblies | engine image | What a derived tool composes from. |
| Entry point `dotnet Elsa.Essentials.Server.dll` | engine image | A derived image replaces it. |

A derived image uses the same version tag as its base. The engine host does not load lists or packages a derived
image adds (R-12).

## `elsaworkflows/elsa-studio-essentials`

| Setting | Purpose |
|---|---|
| `Studio:BackendBaseUrl` | The engine's URL as the browser reaches it. |

Configured through `/app/config/appsettings.json` or environment variables. Its composition is baked; there is no
mounted `shells.json`.
