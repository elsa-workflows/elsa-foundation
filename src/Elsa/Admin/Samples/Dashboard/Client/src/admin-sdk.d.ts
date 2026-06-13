declare module "@elsa-workflows/admin-sdk" {
  import type { ComponentType } from "react";

  export interface ElsaAdminModuleApi {
    readonly navigation: { add(contribution: { id: string; label: string; path: string; order?: number }): void };
    readonly routes: { add(contribution: { id: string; label: string; path: string; component: ComponentType }): void };
    readonly dashboardWidgets: { add(contribution: { id: string; title: string; order?: number; component: ComponentType }): void };
  }
}
