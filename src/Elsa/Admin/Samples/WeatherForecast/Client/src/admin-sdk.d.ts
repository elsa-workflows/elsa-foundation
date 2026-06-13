declare module "@elsa-workflows/admin-sdk" {
  import type { ComponentType } from "react";

  export interface ElsaAdminModuleApi {
    readonly host: {
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
      };
    };
    readonly navigation: { add(contribution: { id: string; label: string; path: string; order?: number }): void };
    readonly routes: { add(contribution: { id: string; label: string; path: string; component: ComponentType }): void };
  }
}
