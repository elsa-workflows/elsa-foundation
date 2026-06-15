export interface StudioRuntimeConfig {
  backendBaseUrl?: string;
}

declare global {
  interface Window {
    __ELSA_STUDIO_RUNTIME__?: StudioRuntimeConfig;
  }
}

export function getStudioRuntimeConfig(): StudioRuntimeConfig {
  return window.__ELSA_STUDIO_RUNTIME__ ?? {};
}
