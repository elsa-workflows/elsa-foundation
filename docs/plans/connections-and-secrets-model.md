# Connections and Secrets: conceptual model

**Status:** Proposed. Decisions D1-D10 below were made in review on 2026-09-30; the rest of the document is still a proposal. Nothing is built.
**Date:** 2026-09-30
**Scope:** `elsa-foundation` (backend, SDK) and `elsa-foundation-studio` (designer UI)
**Follow-ups if accepted:** one ADR (next free number is 0072, but 0001/0002/0003/0050/0062 have been reused, so check for collisions), glossary entries in `docs/glossary/elsa.md` and Studio `CONTEXT.md`, then Speckit specs per phase.

---

## Decisions from review (2026-09-30)

These override anything below that reads differently.

| # | Decision |
|---|---|
| D1 | **Phase 0 ships first, standalone:** wire `Secret` expression evaluation, add the sensitive-input guard. **Amended 2026-10-01 (owner decision).** Phase 0 also gives `SendHttpRequest` an interim credential input `Authorization`, sent as the request's `Authorization` header, so one built-in consumes a secret end to end. Phase 1's `http` connection replaces that input and removes it; Elsa 4 is unreleased, so no compatibility path is kept. |
| D2 | **OAuth callback lives on the backend** (`/_elsa/connections/oauth/callback`) with a host-configurable `PublicBaseUrl`. OAuth stays in the plan as phase 2. |
| D3 | **OAuth is one authentication scheme, not the model.** Schemes are an open plug-in contract (section 2.1a). The OAuth client id and secret are ordinary fields of the OAuth scheme. A host-level default app is a phase 2 convenience. |
| D4 | **Revised 2026-09-30.** **The database is the only runtime source** of connection definitions, but the model splits **definition** (name, type, scheme, settings, secret-field references) from **state** (grant, status, last tested, last used). State is always written at runtime, so it always lives in the DB. Every connection carries a reserved `ManagedBy` field, where only `studio` is valid in v1. The config-to-DB reconciler (`ManagedBy = config`, read-only in Studio, overwritten on each start, pattern of the existing startup reconcilers) is deferred until needed. Only secret *payloads* are pluggable. Rejected: a live multi-source catalog (two lookup paths, collision rules). Earlier draft said config connections could not support OAuth; that only held when a connection was one blob, and the split removes the objection. |
| D5 | **User-delegated connections deferred, seam reserved.** Requirement and resolver carry an owner/run-as field from day one; only `tenant` is valid in v1. |
| D6 | **Separate `Elsa.Connections` feature** depending on `Elsa.Secrets`. Check its core vs extension classification against the `extensions/` split (#1815) when specifying. |
| D7 | **Publish gate:** missing or wrong-type connection blocks publish. A connection in `NeedsAuthorization` only warns. Runs fault with typed `ConnectionUnavailable`. |
| D8 | **Naming:** keep "Connection" in code and UI, and define it in the glossary as **"Service connection"** to separate it from flowchart edges, database connection strings, and `RuntimeConnectionToken`. |
| D9 | **Environments:** same name in every environment. The environment decides what stands behind the name, through the connection's own settings and its secret fields' stores. No mapping layer. |
| D10 | **Enforce `RequiresEncryption` in phase 0** through the sensitive-input guard. Check existing usages when writing the phase 0 spec. Fix spec 079's `ISecretResolver` to the real `ISecretValueResolver` when phase 0 touches it. |

**Verified since the first draft:** a cross-node lock abstraction exists (`IDistributedLockProvider`, `src/Elsa/Locking/Core/`), but the only shipped implementation is filesystem-based. Single-flight token refresh therefore cannot rely on the lock alone. Section 2.6 now requires the Grant write to be a compare-and-swap on the expected secret version, with the lock as an optimization.

---

## 1. Assessment: what exists today

### 1.1 Foundation

| Area | State | Where |
|---|---|---|
| Secrets module | **Implemented.** Named, versioned, typed secrets. Types `text`, `rsa-key`, `x509-certificate`. Lifecycle: active, revoked, expired; rotate; test. Stores: `EncryptedSecretStore` (AES-GCM key ring, `v2:<keyId>:...`) and `ConfigurationSecretStore` (reads `IConfiguration`). Audit sink logs no values. Groundwork persistence on sqlite/postgres/sqlserver/mongo. | `src/Elsa/Secrets/`, `docs/secrets-key-rotation.md`, `specs/079-secrets-module/` |
| Pluggable stores | **Seam exists** (`ISecretStore`, `ISecretStoreRegistry`), **no external store ships.** No KeyVault, AWS, or HashiCorp implementation. | `src/Elsa/Secrets/Core/Contracts/` |
| `SecretReference(Name, TypeName?, Scope?)` + `ISecretValueResolver` | Implemented. Resolves at point of use, tenant-scoped, safe failure codes. | `Core/Models/SecretModels.cs`, `Services/DefaultSecretValueResolver.cs` |
| `Secret` expression syntax | **Descriptor only.** `SecretExpressionDescriptor` (`EditingMode.Reference`, UI hint `SecretPicker`). I grepped `src/` outside `Secrets/`: **nothing references `ISecretValueResolver` or `SecretExpressionTypes`.** A workflow cannot actually consume a secret at runtime today. | `src/Elsa/Secrets/Expressions/` |
| Connections / connectors | **Absent.** No concept, no term in the glossary. | n/a |
| Outbound OAuth (token acquire, refresh, revoke) | **Absent.** The only OAuth code is Identity, where Elsa is the IdP. | `src/Elsa/Foundation/Identity/` |
| HTTP activity auth | `SendHttpRequest` has only a free-form `RequestHeaders` dictionary. No auth model. The only way to use an API key is to paste it into a header literal. | `src/Elsa/Activities/Http/Activities/SendHttpRequest.cs:74-134` |
| AI provider keys (Anthropic, Copilot) | Read from options or environment variables. Not the Secrets module. | `src/Elsa/Agent/*/Options/` |
| Value sensitivity | Partial. `ActivityValuePolicy` and `ValueEnvelope` carry `IsSensitive`, `RequiresEncryption`, `RedactionMode`, but **nothing enforces `RequiresEncryption`**. `[ActivityInput]` has **no sensitive flag**. | `Activities/Runtime/Core/Models/ActivityContract.cs:341`, `Workflows/Runtime/Core/Models/ValueEnvelope.cs` |
| Redaction | Layered heuristics: name-fragment matching in diagnostic snapshots, string-list replacement in agent logs, evidence redaction (`RedactSensitiveValues = true`), OTel redactor seam. Good seams, but all are *detect-and-mask*, not *by-construction*. | `DefaultDiagnosticSnapshotFactory`, `AgentLogRedaction`, `ExecutionEvidenceOptions`, `OpenTelemetryRedactor` |
| Definition serialization | Bindings are immutable, role-owned (ADR 0045). **No definition-level guard against a secret pasted as a literal.** | `docs/serialization.md`, ADR 0045/0046/0062 |
| Tenancy | `Secret.TenantId` is a plain string. Identity carries a tenant claim. No general tenant service. | `Secret.cs`, Identity |

There is also spec drift to note: the spec 079 runtime contract names `ISecretResolver`, while the code has `ISecretValueResolver`.

### 1.2 Studio

| Area | State | Where |
|---|---|---|
| Secrets management UI | **Implemented.** List, detail, create, rotate, revoke, test. Password-type inputs with `autoComplete="new-password"`. "The current value is never shown." | `src/extensions/Elsa.Studio.Secrets/Client/src/` |
| Secret picker | **Implemented.** Expression editor for `syntax === "Secret"`. Emits an opaque reference, never a value. Handles an "unavailable reference" state. | `SecretPickerEditor.tsx`, `module.tsx:39-48` |
| Connections UI, OAuth popup/redirect | **Absent.** | n/a |
| Masked property editor | **Absent.** No built-in password editor, and `uiHint: "password"` is not handled. `SecretSettingEditor` exists only for FeatureManagement settings. | `propertyEditors.tsx`, `SettingEditors.tsx` |
| `isSensitive` on descriptors | Partly wired: flows through `activityInputWire.ts`, "Protected value" shown in runtime views. **Missing from the SDK `StudioActivityInputDescriptor` type.** | `activityInputWire.ts:38` |
| Tenant/workspace UI | **Absent.** Secrets have a free-text `scope` field. | n/a |
| Extension model | Good. `api.propertyEditors.add(...)` and `api.expressionEditors.add(...)`. A new module needs a `StudioModule` feature and a package under `src/extensions/<Name>/`. | `registry.ts`, `sdk/index.ts` |

### 1.3 Where the gaps hurt

- **Activity author DX.** There is no contract for "this activity needs a Google Drive connection." An author today would invent an options class, an env var, or a free-text input. The secret resolver exists but nothing tells the author how to reach it from an activity.
- **Workflow author UX.** An author can create a secret, but cannot say "OneDrive as Alice" or "the Stripe production account," and cannot see which workflows depend on what. OAuth is impossible without hand-pasting tokens, which then cannot refresh.
- **Security posture.** The foundations are good (values never returned, encrypted, audited, versioned). The holes are: (a) secrets are not usable from workflows, so people paste literals; (b) nothing stops a literal secret from being serialized into a definition or git export; (c) `RequiresEncryption` is declared and unenforced; (d) redaction is heuristic; (e) nothing prevents a credential from being sent to an arbitrary host by anyone who can edit a workflow.
- **Testability.** No fake connection or fake token provider exists to test activities that need credentials. Testing token refresh needs a controllable clock, which `TimeProvider` gives us if we use it.

**Bottom line:** the Secrets module is a solid bottom layer, but it is a *dead-end layer today*. The story stops one level too low. The proposal adds the layer above it and finishes wiring the layer below.

---

## 2. Proposed model

### 2.1 Vocabulary

Existing terms stay as they are. New terms are marked **new**.

| Term | Definition |
|---|---|
| **Secret** (exists) | Named, versioned, typed opaque material held by a Secret Store. Knows nothing about what it is used for. |
| **Secret Store** (exists) | Where secret payloads live: encrypted DB, configuration, and (new) KeyVault, AWS Secrets Manager, HashiCorp Vault. |
| **Connection Type** (**new**) | Code-declared descriptor of *a kind of external system connection*: its settings schema, which fields are sensitive, which authentication schemes it supports, its allowed hosts policy, and how to test it. Contributed by a provider or connector package. Examples: `http`, `google-drive`, `onedrive`, `anthropic`, `smtp`. |
| **Connection** (**new**) | An administrator-created, named, tenant-owned instance of a Connection Type. Holds *non-secret settings* plus *references to Secrets* (or, for OAuth, to a Grant). This is the unit users create, share, test, rotate, and revoke. |
| **Authentication Scheme** (**new**) | How a Connection proves identity to the remote system: `api-key`, `basic`, `bearer`, `oauth2-authorization-code`, `oauth2-client-credentials`, `service-account`, `custom`. A Connection Type lists the schemes it supports. The Connection picks one. |
| **Grant** (**new**) | The mutable OAuth token state of a Connection: access token, refresh token, expiry, granted scopes. Stored as a versioned Secret of type `oauth2-grant`. Never edited by hand. |
| **Connection Reference** (**new**) | The only thing that appears in workflow JSON: `{ "connection": "<technical-name>", "type": "google-drive" }`. Same shape philosophy as `SecretReference`. |
| **Connection Requirement** (**new**) | An activity's declaration that an input needs a Connection of a given type (optionally with required scopes or capabilities). |
| **Resolved Connection** (**new**) | The runtime object an activity receives: typed, short-lived, disposable, with redacting `ToString`. |

**Why not "Credential."** Studio `CONTEXT.md` already says Secret should not be called Credential, and the glossary uses "host credential" and "IAM credential" for unrelated things. "Credential" stays informal prose for "whatever proves identity" and is not a model term.

**Collision to flag.** "Connection" already means a flowchart edge (`RuntimeFlowchartLayoutConnectionProjector`), a database `ConnectionString`, and `RuntimeConnectionToken` (Copilot). I propose we own the unqualified term "Connection" for this concept in the glossary, with an `_Avoid_` note, and refer to the others as "flowchart edge" and "database connection string." Worth your call, see section 5.

### 2.1a Authentication schemes are an open contract (D3)

The core knows only the contract, never a specific scheme. A scheme provider declares:

- **Fields:** which are plain settings and which are sensitive (stored as Secrets).
- **Lifecycle capabilities:** whether it has interactive authorization, refreshable state, or revocation. An API key has none. OAuth has all three.
- **An authenticator:** the code that applies the credential to a request or client.

`api-key`, `basic`, `bearer`, and `service-account-json` ship as ordinary schemes. `oauth2-authorization-code` and `oauth2-client-credentials` are ordinary schemes that bring the token-refresh machinery with them. A connector author can add SigV4, mTLS, HMAC signing, or a custom handshake without touching core. The `AuthScheme.OAuth2AuthorizationCode(...)` factory in the example below is shorthand for "a registered scheme provider," not a closed list. A Connection Type is then a set of *supported schemes* plus service-specific settings.

### 2.2 The layering

```
Workflow JSON            ConnectionReference  ("google-drive-marketing")      <- no material
      |
Activity                 Connection Requirement (type = google-drive)
      |  resolve at point of use (tenant-scoped)
Connection               non-secret settings  + secret-field refs + auth scheme
      |
Secret / Grant           versioned, typed, opaque                              <- material
      |
Secret Store             encrypted DB | config/env | KeyVault | AWS SM | Vault
```

Each layer only knows the one below it. Activities never touch a Secret Store. Workflow JSON never contains anything from below the first line.

### 2.3 Declaring, storing, and referencing connections

**Declaring a type (backend, provider package).**

```csharp
public sealed class GoogleDriveConnectionType : ConnectionType
{
    public override string Name => "google-drive";
    public override string DisplayName => "Google Drive";
    public override IReadOnlyList<AuthScheme> Schemes => [
        AuthScheme.OAuth2AuthorizationCode(
            authorizeUrl: "https://accounts.google.com/o/oauth2/v2/auth",
            tokenUrl: "https://oauth2.googleapis.com/token",
            revokeUrl: "https://oauth2.googleapis.com/revoke",
            defaultScopes: ["https://www.googleapis.com/auth/drive.file"]),
        AuthScheme.ServiceAccount(keyType: "google-service-account-json"),
    ];
    // Settings schema (non-secret). Sensitive fields are declared, not inferred.
    public override SettingsSchema Settings => ...;
    public override AllowedHosts Hosts => AllowedHosts.Fixed("*.googleapis.com");
    public override Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct) => ...;
}
```

Types are discovered through the same descriptor pipeline as activities and expression types, so Studio renders the create form from the schema, with no per-type UI code.

**Storing a connection.** A new `ConnectionRepository` (Groundwork, same provider matrix as Secrets) holds two records, because definition and state have different authors (a person or a file writes the definition; the runtime writes the state):

```
ConnectionDefinition { TenantId, Name (immutable technical name), DisplayName, TypeName,
                       AuthScheme, Settings (JSON, non-secret only),
                       SecretFields { fieldName -> SecretReference },
                       ManagedBy (studio in v1; reserved for config/file), Owner (tenant in v1), Tags }
ConnectionState      { Name, Status (Ready | NeedsAuthorization | Error | Revoked),
                       GrantRef (versioned oauth2-grant secret, if the scheme has one),
                       LastTestedAt, LastUsedAt, CreatedAt }
```

**Key decision: sensitive connection fields are stored as Secrets, not inside the connection row.** When you save an API key on a connection, the Connection Manager writes it as a Secret named `connection/<name>/<field>`, created through `ISecretManager`, marked *owned by connection* (hidden from the general secrets list, deletable only with the connection), and stores only the reference. Consequences: one encryption path, one audit path, one rotation mechanism, and free store pluggability. A connection can also point a field at an *existing* shared secret ("use secret `prod-stripe-key`"), useful when ops manages secrets externally.

**Referencing.** By *technical name*, like secrets. A workflow says `google-drive-marketing`. Each environment (dev, staging, prod, another tenant) defines its own connection with that name and type, and the workflow moves between them unchanged. This is the property that makes export, git storage (ADR 0034), and promotion work. Trade-off in section 4.

### 2.4 Activity contract and SDK

Activity authors declare a requirement, they do not fetch secrets:

```csharp
[Activity("Elsa.GoogleDrive", "UploadFile")]
public sealed class UploadFile : Activity
{
    [ActivityConnection("connection", ConnectionType = "google-drive",
        RequiredCapabilities = ["files.write"])]
    public ConnectionReference Connection { get; set; } = default!;

    [ActivityInput] public string Path { get; set; } = default!;
    [ActivityInput] public Stream Content { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityContext ctx)
    {
        await using var conn = await ctx.Connections.ResolveAsync<GoogleDriveClientFactory>(Connection);
        var drive = conn.CreateClient();   // token attached and refreshed inside; activity never sees it
        ...
    }
}
```

Design points:

1. **`[ActivityConnection]` is a new input kind,** not a string input. The runtime knows it is a connection requirement, so it goes into the activity contract and is visible to the designer, the publish gate, and dependency queries ("which workflows use connection X").
2. **The value is a `ConnectionReference` carried through the existing reference-syntax machinery:** a `Connection` expression descriptor with `EditingMode.Reference` and `UIHint = ConnectionPicker`, exactly mirroring `SecretExpressionDescriptor`. This reuses binding, serialization, and Studio's reference-editor path.
3. **The connection type supplies a typed adapter,** not raw tokens. Resolution yields `IResolvedConnection` with three levels of access:
   - *Preferred:* a type-specific adapter (`HttpConnectionAuthenticator.ApplyTo(HttpRequestMessage)`, a configured `DriveService`). The activity author never handles material.
   - *Escape hatch:* `conn.GetSensitiveAsync("apiKey")` returning a `SensitiveValue`.
   - *Introspection:* `conn.Settings` (non-secret) and `conn.Name`.
4. **`SensitiveValue`** is a struct with `ToString()` returning `[redacted]`, no implicit string conversion, and JSON converter that throws. Redaction by construction for values that flow through activity code. The heuristic redactors remain as a second net.
5. **Existing activities migrate.** `SendHttpRequest` gains an `[ActivityConnection(ConnectionType="http", Optional=true)]`, which replaces and removes its phase 0 `Authorization` credential input (D1); the `http` type supports `api-key` (header or query placement), `basic`, `bearer`, `oauth2-client-credentials`. The Anthropic and Copilot agent options become an `anthropic` and a `github-copilot` connection type. These are the natural first consumers.
6. **Host pinning.** A Connection has an allowed-hosts policy (fixed for `google-drive`, user-declared base URL for `http`). The authenticator refuses to attach material to a request whose host is not allowed. This closes the confused-deputy hole where someone who can *edit a workflow* points `SendHttpRequest` at their own server and harvests the API key.

### 2.5 Secret storage providers

The `ISecretStore` seam already covers this. Proposed additions, each an extension package (per the `extensions/` split, #1815), none in core:

| Store | Notes |
|---|---|
| `EncryptedSecretStore` (exists) | Default. Writable. |
| `ConfigurationSecretStore` (exists) | Covers **environment variables** via `IConfiguration`. Read-only. |
| Azure Key Vault, AWS Secrets Manager, HashiCorp Vault | Store holds the payload, Elsa holds only metadata and a store-native locator. Versions map to the store's native versions where they exist. |

Two constraints the design must state, because they bite later:

- **Read-only stores cannot hold Grants.** OAuth refresh writes a new token version. A connection type declares `RequiresWritableStore` for Grant material, and the manager refuses a read-only store for it. (Vault and KeyVault are writable, so this mostly excludes env and config. Workaround: env supplies the OAuth *client secret*, the Grant lives in a writable store.)
- **Resolver caching.** Remote stores add latency. The resolver gets a short in-process cache keyed by `(tenant, name, version)` with a TTL, invalidated on rotate. Never a shared/distributed cache of cleartext.

### 2.6 OAuth token lifecycle

**Authorization (acquire).** Backend-owned, Studio only opens a window:

1. Studio calls `POST /_elsa/connections/{name}/authorize` and gets an authorization URL.
2. The backend generates `state` (bound to tenant, connection, user, expiry) and a **PKCE** verifier, stored server-side.
3. Studio opens a popup at the provider. The provider redirects to a **backend callback endpoint** (`/_elsa/connections/oauth/callback`), *not* to Studio, so the registered redirect URI is stable and independent of the Studio deployment.
4. The backend validates `state`, exchanges the code, writes the Grant as a new Secret version, sets the connection to `Ready`, and the callback page `postMessage`s completion to the opener and closes.

**Refresh.** On demand, at resolve time:

- If `expiresAt - skew (default 60s) < now`, refresh through `IConnectionTokenProvider`.
- **Single-flight per connection.** Take a per-connection lock through the existing `IDistributedLockProvider`, then re-read the Grant after acquiring it, since another caller may already have refreshed. The lock is an optimization, not the guarantee: only a filesystem implementation ships today, so a multi-node host may have no real cross-node lock. Correctness comes from the Grant write being a **compare-and-swap on the expected secret version**. A losing writer discards its result and re-reads. This matches the rule that stores own integrity (CAS) while the application layer owns the refresh policy. Two nodes refreshing at once burns rotating refresh tokens, so this is not optional.
- If the provider returns a new refresh token (rotation), the new pair is written as one new secret version atomically. Never update the two halves separately.
- Use `TimeProvider` everywhere, so tests can drive expiry with a fake clock.
- Optional background keepalive for providers that expire *unused* refresh tokens (Google and Microsoft both do, on the order of months). Off by default.

**Failure.** `invalid_grant` or revoked-by-user is *permanent*: the connection moves to `NeedsAuthorization`, an item is raised through the existing Attention mechanism, and activities fault with a typed `ConnectionUnavailable` fault (not retried by generic retry strategies, since retrying cannot help). Transient errors (network, 5xx) retry with backoff and do not change status.

**Revocation.** Deleting or revoking a connection: (1) best-effort call to the provider's revocation endpoint, (2) revoke all Grant versions, (3) revoke or delete owned secrets, (4) audit. Provider-side failure does not block local revocation, but is surfaced.

**Client app registration** (the OAuth client id/secret) is an open question, section 5.

### 2.7 Studio UX

**Connections page** (`/security/connections`, next to Secrets):
- "New connection" opens a gallery of Connection Types (from descriptors), then a form generated from the type's schema. Sensitive fields render masked with a "Replace" affordance, never prefilled.
- OAuth types show an **Authorize** button that opens the popup, and a status chip (Ready, Needs authorization, Error). API-key types show **Test connection**.
- Detail view: settings, status, last tested, last used, **used by** (workflows and activities that reference it), rotate/replace, revoke, delete (blocked with a clear list while referenced by published workflows).

**In the activity panel:** a connection input renders a **Connection picker** filtered by required type and capabilities, with an inline **"+ New connection"** that opens the create dialog in place and selects the result. Same registration path as the Secret picker (`api.expressionEditors.add` for the `Connection` syntax).

**Workflow-level "Connections" panel** (next to Variables): lists every connection the workflow requires, resolved or missing in this environment. On import into an environment where a name is missing, show *"Create"* or *"Map to existing connection of the same type"* (mapping rewrites the reference, it is an explicit author action, never automatic).

**Publish gate:** referenced connections must exist and match type. Missing ones block publish with the list. Whether a connection in `NeedsAuthorization` blocks publish or only warns is a policy choice (section 5).

**Prerequisite cleanup in Studio:** add `isSensitive` to the SDK descriptor type, and add a masked property editor honoring `uiHint: "password"` for the rare non-reference sensitive input.

### 2.8 Multi-tenancy and scoping

Resolution is always **within the workflow instance's tenant.** A reference can never reach another tenant's connection. Enforcement lives in the resolver and repository, not in each caller.

Proposed scope ladder, in priority order:

1. **Tenant-owned, shared (v1, default).** Created by someone with `connections:manage`, usable by any workflow in the tenant whose *author* holds `connections:use` on it at bind and publish time.
2. **Restricted.** The same connection with an allow-list of workflow definitions or tags. Use this instead of copying connections per workflow.
3. **Per-workflow copies: not a scope.** A "workflow-scoped connection" is just a tenant connection restricted to one workflow. Avoids a second lookup rule.
4. **User-delegated (v2, needs your input).** "OneDrive as Alice" for workflows triggered by a human. Runs into the unattended-execution problem: whose token when a timer fires at 3 a.m.? Proposal: defer, and when it lands, make it an explicit `runAs: invoker | owner` on the requirement, failing closed if there is no invoker.

Permissions: `connections:read` (metadata), `connections:use` (bind and publish), `connections:manage` (create, rotate, revoke, delete). Runtime execution itself is by the engine, not the end user, so `use` is checked at *authoring and publish time*, not per run.

### 2.9 Serialization: never leak material

Defense in depth, ordered from structural to heuristic:

1. **Structural.** Connection inputs can only hold a `ConnectionReference`. The type carries name and type only. There is no field a token could occupy.
2. **Sensitive-input guard.** Add `Sensitive` (or `SecretOnly`) to `[ActivityInput]` and enforce it in the definition validator: a literal bound to a sensitive input is rejected at save, publish, and import, with an error pointing at the input and suggesting Secret or Connection. This finally puts `RequiresEncryption`/`IsSensitive` to work. Applies to git export (ADR 0034) as well, since it goes through the same serializer.
3. **By-construction redaction at runtime.** `SensitiveValue` for anything that passes through activity code. Execution evidence records *connection name, type, resolved secret version, and outcome*, never material. Journal and persisted state store references only.
4. **Heuristic nets stay.** Name-fragment redaction, `AgentLogRedaction`, OTel redactor. Add: values resolved during an activity are registered with a scoped redactor for that execution, so an accidental log line of a token is masked even when its name looks innocent.
5. **Canary test.** One end-to-end test that creates a connection with a canary secret, runs a workflow that uses it (including a failing run and a refresh), then scans definition JSON, git export, journal, persisted instance state, execution evidence, logs, diagnostics snapshots, and OTel output for the canary. Any hit is a red build. This is the test that keeps the guarantee true after the next 50 PRs. It gets a bite-proof (deliberately leak, confirm red).

---

## 3. Testability

- **Fakes in the test kit:** `InMemoryConnectionType`, `FakeConnectionResolver` (activities under test receive a scripted `ResolvedConnection`), and `FakeOAuthProvider` (an in-process token endpoint that can expire, rotate, and return `invalid_grant`).
- **Refresh logic** tested with `FakeTimeProvider` and a concurrent-callers test (N callers, exactly one refresh).
- **Store contract tests:** one shared case table run against every `ISecretStore` and every persistence provider, the pattern that already found real divergence across providers.
- **Connection type conformance test:** every registered type must declare its sensitive fields, host policy, and a `TestAsync`, checked by an arch-style guard so a new connector cannot ship without them.

---

## 4. Alternatives considered

**A. Connection is just a Secret with a JSON blob type.** Simplest, and reuses everything. Rejected: no place for non-secret settings (tenant id, base URL, region) that the UI must display and the audit must show; no typed contract for activities; OAuth refresh would mutate a blob through a generic path; the picker cannot filter "a Drive-capable thing". You would rebuild Connection on top of it anyway, badly.

**B. Credentials inline on the activity (a "username/password" pair of inputs).** Easiest for a demo. Rejected outright: duplicates secrets per activity, no reuse, no rotation, and it is precisely how material ends up in workflow JSON.

**C. Layered: Connection above Secret (chosen).** More concepts, but each has one job, and the existing Secrets investment is reused instead of replaced.

**D. Connection owns its own encrypted storage (no Secret rows behind it).** Fewer moving parts per connection. Rejected: forks the encryption, audit, rotation, and store-pluggability story into two systems. Storing connection sensitive fields as owned Secrets costs one naming convention and one visibility flag.

**E. Reference by technical name (chosen) vs by ID.** Names make workflows portable across environments and readable in git diffs; the cost is that renaming is disallowed (name immutable, display name editable), which matches how Secrets already work. IDs would force a mapping step on every promotion.

**F. New `Connection` expression syntax vs a dedicated input kind (chosen: both, layered).** A syntax alone cannot express "this input *requires* a connection of type X," so the publish gate and dependency queries would have to guess. A dedicated input kind alone would duplicate the reference editor plumbing. So: the input kind carries the contract, the reference syntax carries the value.

**G. Typed adapter vs handing activities the token.** Handing over tokens is easier to write and impossible to make safe (the activity can log, store, or forward it). Adapters keep material inside the connection type, where host pinning and refresh live. The escape hatch exists but is explicit and greppable.

**H. OAuth refresh: on-demand (chosen) vs background refresher.** A refresher keeps tokens warm but adds a hosted service, a failure mode, and leader-election. On-demand needs only single-flight. The one thing on-demand misses is refresh tokens that expire from disuse, hence the optional keepalive.

**I. Delegating everything to an external broker (Nango, Vault dynamic secrets).** Attractive for the long tail of SaaS OAuth. Not core: it can be implemented as a Connection Type or Secret Store extension later, and the model above does not preclude it.

**J. Per-workflow connection scope.** Rejected as a distinct scope in favor of tenant connections with an allow-list (section 2.8), to keep one resolution rule.

---

## 5. Open questions

The original eleven questions are resolved as D1-D10 above, with one exception (Q11, below, was a verification item and is now answered). What remains:

1. **Same-name collision across a rename.** Names are immutable, so a rename means create-new and re-bind. Is that acceptable, or do you want an alias mechanism? (Secrets behave the same way today.)
2. **Host-level default OAuth app** (phase 2 convenience): configuration shape and whether a connection may override it. Deferred until phase 2 is specified.
3. **Apply-from-file import** for GitOps users: CLI or API, and whether it can also create the connection's secret fields from references. Deferred, not blocking.
4. **Multi-node lock reality.** Which production lock provider will hosts use? The CAS on Grant writes makes refresh correct regardless, but contention behavior depends on it.
5. **Not yet read in depth:** ADR 0045 binding internals (how a new `Connection` input kind slots into role-owned bindings) and the Secrets API endpoints. Check both when writing the phase 0 and phase 1 specs.

## 6. Suggested phasing (if accepted)

| Phase | Unit | Notes |
|---|---|---|
| 0 | Wire `Secret` expression evaluation; add `Sensitive` flag to `[ActivityInput]` and definition guard; add `isSensitive` to Studio SDK descriptor; canary test v1; interim credential input `Authorization` on `SendHttpRequest` (D1, amended 2026-10-01) | No new concepts. Closes today's real leak risk. |
| 1 | Connections core: types, repository, manager, resolver, `SensitiveValue`, `[ActivityConnection]`, publish gate, permissions; `http` type (api-key, basic, bearer); migrate `SendHttpRequest`, replacing and removing its phase 0 `Authorization` input | Studio: Connections page, picker, workflow Connections panel. |
| 2 | OAuth: authorization-code with PKCE, client credentials, Grant secret type, single-flight refresh, revocation, Attention integration, `FakeOAuthProvider` | Needs answers to questions 2 and 3. |
| 3 | External stores: KeyVault first, then AWS SM, Vault; store contract suite | Independent of phase 2. |
| 4 | First real connectors (Google Drive, OneDrive), migrate Anthropic and Copilot options to connection types | Proves the SDK ergonomics on real providers. |
| Later | User-delegated connections; environment mapping; external broker adapter | Decide shape of resolver now, build later. |
