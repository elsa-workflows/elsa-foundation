declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface ElsaStudioModuleApi {
    readonly host: {
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
      };
    };
    readonly navigation: { add(item: { id: string; label: string; path: string; order?: number; iconColor?: string }): void };
    readonly routes: { add(item: { id: string; label: string; path: string; component: ComponentType }): void };
  }
}
