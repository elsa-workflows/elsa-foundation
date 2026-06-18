# Contract: Studio Auth SDK

This contract defines the provider-agnostic authentication surface consumed by
`elsa-foundation-studio` (React/Vite shell and feature modules). It generalizes
the proven Blazor `Authentication.Abstractions` shape (provider manager +
adapters + authenticating transport) for the React shell. The deprecated
`Elsa.Studio.Login` pattern is explicitly NOT carried forward.

## Module-facing rules

- Modules consume auth state and actions only through the exported SDK
  (`useAuthSession`, `usePermissions`, `AuthGuard`, transport interceptors).
- Modules MUST NOT read tokens directly, construct provider-specific login URLs,
  or branch on a specific provider/ownership mode.
- Permission-gated UI MUST derive from the normalized permission set, never from
  provider claims directly.
- Studio authorization is for presentation only; the backend remains the
  enforcement source of truth (a hidden action is not a security boundary).

## Core types

### `AuthSession`

```ts
interface AuthSession {
  status: "unknown" | "anonymous" | "authenticated";
  subject?: string;
  displayName?: string;
  tenantId?: string;
  roles: string[];
  permissions: string[];      // normalized permission keys
  tokenFreshness?: "fresh" | "stale" | "expired";
  provider?: { id: string; kind: string };
}
```

### `AuthCapabilities`

Mirror of the backend capabilities endpoint, used to drive management UX:

```ts
interface AuthCapabilities {
  ownershipMode: "foundation-owned" | "external-owned" | "hybrid";
  supportsLocalUserManagement: boolean;
  supportsLocalRoleManagement: boolean;
  supportsApplicationManagement: boolean;
  supportsGroupSync: boolean;
  supportsTokenIssuance: boolean;
}
```

## Provider abstraction

### `AuthProviderAdapter`

Each provider profile (external OIDC, OpenIddict/local, legacy compatibility)
ships an adapter implementing one interface:

```ts
interface AuthProviderAdapter {
  id: string;
  kind: string;
  initialize(): Promise<AuthSession>;     // restore/probe existing session
  login(options?: LoginOptions): Promise<void>;   // redirect or interactive
  handleCallback(): Promise<AuthSession>; // OIDC redirect return
  logout(): Promise<void>;
  getAccessToken(): Promise<string | null>;
  refresh(): Promise<AuthSession>;
}
```

### `AuthProviderManager`

- Selects the active adapter (from backend capabilities/default provider).
- Exposes a single `login/logout/getAccessToken/refresh` facade to the shell.
- Modules never see the adapter directly.

## React surface

- `AuthProvider` — context provider wired to the active adapter; bootstraps
  session on load and after redirect callbacks.
- `useAuthSession(): AuthSession` — reactive session state.
- `usePermissions(): { has(key): boolean; hasAny(keys): boolean }` — permission
  checks over the normalized set.
- `useAuthCapabilities(): AuthCapabilities` — capability gates for management UI.
- `<AuthGuard requires="workflows.read" fallback={...}>` — declarative gating.
- `<RequireAuth>` — route-level protection that triggers `login()` when anonymous.

## Transport integration

- HTTP: an interceptor attaches the access token to backend calls, transparently
  refreshes on `401` when the adapter supports refresh, and surfaces a re-auth
  challenge on hard failure.
- SignalR/realtime: a token provider supplies the access token to the connection
  and reconnects with a fresh token after refresh.
- Token acquisition MUST go through `AuthProviderManager.getAccessToken()`; no
  module-local token caching.

## Capability-driven management UX

- Users/roles/applications/providers screens consume `useAuthCapabilities()` and
  render actions as enabled, disabled, or read-only accordingly.
- When the backend returns `capabilityDisabled`, the UI MUST present an
  explanatory read-only state, not an error toast.
- In `external-owned` mode, local create/edit affordances for IdP-owned entities
  MUST be hidden or disabled with a clear reason.

## Non-goals

- No bespoke username/password login screen as the default path; local-credential
  login exists only via the OpenIddict/local or legacy compatibility adapter.
- No provider-specific UI baked into the shell; provider differences live entirely
  inside adapters.
