declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioHttpClient {
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
    postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
  }

  export interface StudioEndpointContext {
    baseUrl: string;
    http: StudioHttpClient;
  }

  export interface StudioNavigationContribution {
    id: string;
    label: string;
    path: string;
    order?: number;
    iconColor?: string;
  }

  export interface StudioRouteContribution {
    id: string;
    path: string;
    label: string;
    component: ComponentType;
  }

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

  export interface ElsaStudioModuleApi {
    readonly backend: StudioEndpointContext;
    readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
    readonly routes: StudioContributionRegistry<StudioRouteContribution>;
  }
}
