declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface ElsaStudioModuleApi {
    readonly host: {
      readonly baseUrl: string;
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
      };
    };
    readonly backend: {
      readonly baseUrl: string;
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
      };
    };
    readonly panels: { add(item: { id: string; title: string; order?: number; component: ComponentType }): void };
  }
}
