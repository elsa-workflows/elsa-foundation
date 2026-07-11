export { createAuthProviderManager, AuthConfigurationError } from "./AuthProviderManager";
export { createBackendAuthProviderManager } from "./backend";
export { AuthProvider } from "./AuthProvider";
export { useAuthSession } from "./useAuthSession";
export { usePermissions } from "./usePermissions";
export { useAuthCapabilities } from "./useAuthCapabilities";
export { useAuthContext } from "./AuthContext";
export { AuthGuard } from "./guards/AuthGuard";
export { RequireAuth } from "./guards/RequireAuth";
export type { RequireAuthErrorFallbackProps, RequireAuthProps } from "./guards/RequireAuth";
export { createRedirectAuthAdapter, AuthAdapterError } from "./adapters/redirect";
export { createOidcAuthAdapter } from "./adapters/oidc";
export { createAuthenticatedHttpClient } from "./transport/http";
export { createSignalRAccessTokenFactory, withAuthenticatedSignalROptions } from "./transport/signalr";
export type {
  AuthBootstrap,
  AuthBootstrapProvider,
  AuthCapabilities,
  AuthChallenge,
  AuthContextValue,
  AuthProviderAdapter,
  AuthProviderManager,
  AuthProviderProps,
  AuthProviderRef,
  AuthProviderSummary,
  AuthSession,
  AuthSessionStatus,
  IdentityAuthority,
  LoginOptions,
  OwnershipMode,
  PermissionSet,
  ProviderCapabilities,
  TokenFreshness
} from "./types";
