import type { ReactNode } from "react";

export type AuthSessionStatus = "unknown" | "anonymous" | "authenticated";
export type TokenFreshness = "none" | "fresh" | "refreshing" | "expired";
export type OwnershipMode = "foundation-owned" | "external-owned" | "hybrid";
export type IdentityAuthority = "foundation" | "external";

export interface AuthSession {
  status: AuthSessionStatus;
  subject?: string | null;
  displayName?: string | null;
  tenantId?: string | null;
  roles: string[];
  permissions: string[];
  tokenFreshness?: TokenFreshness;
  provider?: AuthProviderRef | null;
}

export interface AuthProviderRef {
  id: string;
  kind: string;
}

export interface AuthCapabilities {
  ownershipMode: OwnershipMode;
  userAuthority?: IdentityAuthority;
  roleAuthority?: IdentityAuthority;
  applicationAuthority?: IdentityAuthority;
  capabilities?: ProviderCapabilities;
  providers: AuthProviderSummary[];
}

export interface ProviderCapabilities {
  supportsLocalUserManagement: boolean;
  supportsLocalRoleManagement: boolean;
  supportsApplicationManagement: boolean;
  supportsGroupSync: boolean;
  supportsTokenIssuance: boolean;
  supportsRefresh: boolean;
  supportsRevocation: boolean;
}

export interface AuthProviderSummary {
  id: string;
  kind: string;
  displayName?: string;
  isDefault: boolean;
  enabled: boolean;
  capabilities?: ProviderCapabilities;
}

export interface AuthBootstrap {
  ownershipMode: OwnershipMode;
  providers: AuthBootstrapProvider[];
}

export interface AuthBootstrapProvider extends AuthProviderSummary {
  displayName?: string;
  challenge?: AuthChallenge;
}

export type AuthChallenge =
  | { url: string; method: "GET" | "POST" | string; scheme?: string; parameters?: Record<string, unknown> }
  | { type: "redirect"; loginPath: string }
  | { type: "none" };

export interface LoginOptions {
  providerId?: string;
  returnUrl?: string;
}

export interface AuthProviderAdapter {
  id: string;
  kind: string;
  initialize(): Promise<AuthSession>;
  login(options?: LoginOptions): Promise<void>;
  handleCallback(): Promise<AuthSession>;
  logout(): Promise<void>;
  getAccessToken(): Promise<string | null>;
  refresh(): Promise<AuthSession>;
}

export interface AuthProviderManager {
  getSession(): AuthSession;
  getCapabilities(): Promise<AuthCapabilities>;
  initialize(): Promise<AuthSession>;
  login(options?: LoginOptions): Promise<void>;
  handleCallback(providerId?: string): Promise<AuthSession>;
  logout(): Promise<void>;
  getAccessToken(): Promise<string | null>;
  refresh(): Promise<AuthSession>;
}

export interface AuthContextValue {
  session: AuthSession;
  capabilities: AuthCapabilities | null;
  login(options?: LoginOptions): Promise<void>;
  logout(): Promise<void>;
  refresh(): Promise<AuthSession>;
}

export interface AuthProviderProps {
  manager: AuthProviderManager;
  children: ReactNode;
}

export interface PermissionSet {
  has(key: string): boolean;
  hasAny(keys: readonly string[]): boolean;
  hasAll(keys: readonly string[]): boolean;
}

export const anonymousAuthSession: AuthSession = {
  status: "anonymous",
  roles: [],
  permissions: []
};

export const unknownAuthSession: AuthSession = {
  status: "unknown",
  roles: [],
  permissions: []
};
