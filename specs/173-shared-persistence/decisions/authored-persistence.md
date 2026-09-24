# Authored persistence configuration and presence

Status: selected engineering direction for #1967; Phase 0 review complete; Phase 1 types and integration review remain required.

## Configuration surface

Use the existing Elsa:Persistence namespace. Define resources once at the root and allow a root default, a shell default override, and shell-specific feature bindings:

```json
{
  "Elsa": {
    "Persistence": {
      "Resources": {
        "primary": { "Provider": "PostgreSql", "ConnectionName": "Elsa" },
        "diagnostics": { "Provider": "PostgreSql", "ConnectionName": "ElsaDiagnostics" }
      },
      "DefaultResource": "primary"
    }
  },
  "CShells": {
    "Shells": {
      "Default": {
        "Configuration": {
          "Elsa": {
            "Persistence": {
              "Bindings": {
                "DiagnosticsStructuredLogsEntityFrameworkCore": "diagnostics",
                "DiagnosticsOpenTelemetryEntityFrameworkCore": "diagnostics"
              }
            }
          }
        }
      }
    }
  }
}
```

This is a persistence fragment, not a complete host configuration. Existing Features declarations still enable the consumers; a binding never enables a feature. Connection values remain in the host's existing ConnectionStrings configuration and are omitted from this fragment.

For each enabled enrolled consumer, select the shell binding first, then the shell Configuration:Elsa:Persistence:DefaultResource, then root Elsa:Persistence:DefaultResource, then its legacy configuration. The effective shell default may therefore originate at the root. There are no root feature bindings or shell-local resource catalogs in this first contract. Preserve unknown authored data and report it as unresolved; never interpret an unsupported root Bindings node as a default for every shell.

Resources carry Provider and ConnectionName together; no inline ConnectionString, Schema, Pooling or migration policy field is introduced on a resource. Root and shell configuration providers keep their existing precedence within each key. A shell selection wins over root fallback by scope, including a final code-configured shell default. A higher-priority root value does not override an explicitly present shell key; to override the shell key, address its full shell path.

Names follow case-insensitive configuration lookup while explanations retain stable catalog feature identities. Missing selection means inheritance; removing a key at its owning source restores the next inherited value. Removing only a higher-priority override may reveal a lower-priority value of that same key. Explicit null, blank or unknown selections are invalid, not removal. A malformed selected resource refuses; unused definitions do not activate consumers. Do not infer a resource from its name or from there being only one definition.

First-slice resource names and `ConnectionName` references are identifier-like: they begin with a letter or underscore and continue with letters, digits, underscore, hyphen or dot. `true`, `false` and `null` are reserved case-insensitively. Provider names use the same lexical rule before the existing provider validator checks supported engines. This is a deliberate conservative boundary: standard `IConfiguration` providers expose JSON `false` and `0` as strings, losing the original scalar token type. Rejecting scalar-like spellings whether they were quoted or not makes a malformed selection/referral fail closed without adding a second JSON parser or pretending to know its source token. The restriction applies only to the new resource surface; existing legacy per-feature values keep their current parsing. Unknown unused definitions remain preserved. Broader resource-name syntax would require a typed authored source contract and a separate review.

## Effective authored presence

The pure resolver receives the final composed shell map, explicit root fallback, final feature identities and a narrow presence record for Provider, ConnectionName and ConnectionString. Presence is separate from value. An applicable resource plus any effective authored legacy target field, including null/empty, refuses ambiguity. CLR property initializers are never authored input.

The published CShells hook preserves nested shell Configuration null leaves, but feature settings flattening drops null values. For Workbench, the adapter combines the final hook map with selected-shell traversal of the injected IConfiguration source view for those three fields only. Enumerate children or use provider TryGet; never use Exists() to distinguish absent from explicit null. Support object-map features and array entries with Name plus direct settings or a Settings wrapper. The public CShells blueprint/parser remains authoritative for shape validity; no generic JSON merge or replacement feature parser is added.

FeatureSettingResetIds suppress lower-priority raw feature fields, and directly disabled inactive features do not participate. The final ordered graph remains authoritative: if a dependency reintroduces an explicitly disabled feature, do not drop it from applicability. Report that resource-mode composition conflict before feature construction. Non-null final code/configuration target keys count as authored; opaque feature configurators on resource participants still refuse before invocation.

Collect source data once for the candidate. The runtime adapter must detect a configuration reload while collecting its raw/root inputs and refuse or retry the whole candidate rather than combine partial reads. Exact source-provider provenance is reported only when available; a composed-shell origin is preferable to inventing a file/line origin. This does not claim atomic configuration transactions or the broader #1964 preview/apply concurrency contract.

## Ownership and materialization

Enroll only the 13 stable features listed in the spec through explicit markers on their owning feature classes. Combine the marker with existing UsesEfModule and EfModuleDescriptor metadata, including ContextType, instead of inferring persistence ownership from similarly named properties or adding Workflows type references to the foundation package. Module metadata retains provider support, migration history, connection defaults and dependencies.

The resolver produces only per-feature Provider and ConnectionName materialization plus redacted provenance, conflicts and unresolved prerequisites. It never returns connection values. Preserve Schema, Pooling and all unrelated feature fields; validate shared-context options through EF-owned rules. Runtime connection resolution and live tooling verification remain trusted adapters. Host-owned OpenIddict, unenrolled stores and custom types are not redirected by the root default.

## Executed boundary probe

On 2026-09-23, an isolated package-only net10.0 probe against CShells and CShells.Abstractions 0.0.30-preview.157 confirmed:

- Nested DefaultResource=null and binding=null leaves survive ConfigurationShellBlueprint.ComposeAsync.
- Feature Provider=null disappears from composed feature settings but remains a present null child in the raw IConfiguration view.
- Public FeatureDiscovery.DiscoverFeatures and FeatureDependencyResolver.GetOrderedFeatures discover and expand a two-feature dependency without constructing either feature or a shell service provider. Both feature constructors and ConfigureServices throw if called; neither was called.

Command: `dotnet run --project /tmp/runtime-composition-1967-presence-probe/PackageProbe.csproj --no-restore`. All assertions passed. This characterizes the published package with an in-memory configuration fixture; it is not Elsa adapter, provider-chain concurrency, real-host or database proof. The implementation acceptance suite must preserve these cases and add JSON overlays, environment inputs, arrays/wrappers, resets and reload.

An additional published-package probe compared the shared composer followed by public ShellBuilder.FromConfiguration with the actual configured runtime blueprint. Object overrides, boolean reset and disabled declarations matched across enabled/disabled/reset identities, configuration data and configurator identities. No feature constructor or configurator ran. This validates the public composition seam for those fixtures, not a complete Workbench integration.
