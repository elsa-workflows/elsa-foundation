declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioEndpointContext {
    readonly baseUrl: string;
    readonly headers?: HeadersInit;
    readonly http: {
      getJson<T>(url: string, init?: RequestInit): Promise<T>;
    };
    /**
     * Present only on the authenticated context: returns the current bearer token (or "" when anonymous).
     * Assigned to a SignalR connection's accessTokenFactory so the hub carries the same token as HTTP.
     */
    readonly accessTokenFactory?: () => Promise<string>;
  }

  export interface StudioNavigationContribution {
    id: string;
    label: string;
    path: string;
    activePathPrefix?: string;
    order?: number;
    iconColor?: string;
    parentId?: string;
  }

  export interface StudioRouteContribution {
    id: string;
    label?: string;
    path: string;
    component: ComponentType;
  }

  export interface StudioPanelContribution {
    id: string;
    title: string;
    order?: number;
    component: ComponentType;
  }

  export interface StudioContributionRegistry<T> {
    add(item: T): void;
  }

  export interface ElsaStudioModuleApi {
    readonly host: StudioEndpointContext;
    readonly backend: StudioEndpointContext;
    readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
    readonly routes: StudioContributionRegistry<StudioRouteContribution>;
    readonly panels: StudioContributionRegistry<StudioPanelContribution>;
  }
}
