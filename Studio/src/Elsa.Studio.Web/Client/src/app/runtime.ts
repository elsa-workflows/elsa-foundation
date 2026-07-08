import type { StudioRuntimeSettings, StudioWorkflowRuntimeSettings } from "../sdk";

export interface StudioRuntimeConfig {
  backendBaseUrl?: string;
  backendModuleManagementApiKey?: string;
  auth?: StudioAuthRuntimeConfig;
  workflows?: StudioWorkflowRuntimeSettings;
}

/**
 * Opt-in user-authentication configuration. When absent (the dev/no-provider default) the shell boots
 * anonymously: no login is required and backend calls attach no bearer token. When `enabled` is true a
 * user session is required and the auth subsystem's capability negotiation drives the login flow.
 */
export interface StudioAuthRuntimeConfig {
  enabled?: boolean;
  /**
   * Endpoint the backend provider exchanges the session cookie for a bearer token at. When omitted the
   * auth subsystem falls back to `defaultBackendTokenEndpoint` (`/_elsa/identity/token`). Surfaced from the
   * host via `/studio-runtime.js` so a deployment can point the shell at a different identity backend
   * without rebuilding the SPA.
   */
  tokenEndpoint?: string;
  /**
   * Optional refresh endpoint. When set (together with a client-held refresh token) the redirect adapter
   * posts to it on refresh; when omitted, refresh re-probes the session cookie instead (the cookie flow).
   */
  refreshEndpoint?: string;
}

export function isStudioAuthEnabled(config: StudioRuntimeConfig): boolean {
  return config.auth?.enabled === true;
}

declare global {
  interface Window {
    __ELSA_STUDIO_RUNTIME__?: StudioRuntimeConfig;
  }
}

export function getStudioRuntimeConfig(): StudioRuntimeConfig {
  return window.__ELSA_STUDIO_RUNTIME__ ?? {};
}

export function getStudioRuntimeSettings(config: StudioRuntimeConfig): StudioRuntimeSettings {
  return {
    workflows: config.workflows
  };
}
