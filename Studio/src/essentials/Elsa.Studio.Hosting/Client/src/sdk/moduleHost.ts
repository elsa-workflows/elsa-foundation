import type {ComponentType} from "react";
import type {AuthProviderManager, AuthSession, ElsaStudioModuleApi, StudioBackendSession, StudioBackendAwareContribution, StudioContributionAvailability, StudioEndpointContext} from "./index";

/** A document-owned immutable connection, independent of optional management UI. */
export interface StudioManagedBackendSession extends StudioBackendSession {
  readonly manager: AuthProviderManager | null;
  dispose(): void;
  /** End a document/identity lifetime, including in-flight requests. */
  retire(): void;
}

export interface StudioConnectionSnapshot {
  active: StudioManagedBackendSession | null;
  loading: boolean;
  error: string | null;
}

/** Replacement contract: zero custom controllers uses the host's fixed backend; more than one is refused. */
export interface StudioConnectionController {
  readonly hostContext: StudioEndpointContext;
  readonly hostIdentity: AuthSession;
  getSnapshot(): StudioConnectionSnapshot;
  subscribe(listener: () => void): () => void;
  initialize(): Promise<void>;
  qualifyPath(path: string): string | null;
  dispose?(): void;
}

export interface StudioModuleHostContext {
  readonly host: StudioEndpointContext;
  readonly identity: AuthSession;
  readonly authManager: AuthProviderManager | null;
  readonly configuration: Readonly<Record<string, unknown>>;
  readonly signal: AbortSignal;
}

/** An instance belongs to one host identity/document; register may populate each fresh registry. */
export interface StudioModuleInstance {
  readonly connection?: StudioConnectionController;
  readonly shell?: readonly StudioShellContribution[];
  register(api: ElsaStudioModuleApi): void | Promise<void>;
  dispose?(): void;
}

export interface StudioShellContribution extends StudioBackendAwareContribution {
  availability?: StudioContributionAvailability;
  id: string;
  placement: "connection" | "notice" | "recovery";
  order?: number;
  component: ComponentType<{navigate(path: string): void}>;
}
