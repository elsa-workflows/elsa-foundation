import type { StudioBackendProfile } from "@elsa-workflows/studio-sdk";
export interface BackendRuntimeConfig {
 backendBaseUrl?: string;
 hostId?: string;
 profileApiPath: string;
 backends?: {
  defaultId?: string;
  allowPersonalProfiles?: boolean;
  profiles?: StudioBackendProfile[];
  authenticationProfiles?: Record<string,{enabled?:boolean;tokenEndpoint?:string;refreshEndpoint?:string}>;
 };
}
