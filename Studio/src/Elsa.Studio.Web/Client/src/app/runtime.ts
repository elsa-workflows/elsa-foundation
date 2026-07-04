export interface StudioRuntimeConfig {
  backendBaseUrl?: string;
  backendModuleManagementApiKey?: string;
  auth?: StudioAuthRuntimeConfig;
}

/**
 * Opt-in user-authentication configuration. When absent (the dev/no-provider default) the shell boots
 * anonymously: no login is required and backend calls attach no bearer token. When `enabled` is true a
 * user session is required and the auth subsystem's capability negotiation drives the login flow.
 */
export interface StudioAuthRuntimeConfig {
  enabled?: boolean;
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
