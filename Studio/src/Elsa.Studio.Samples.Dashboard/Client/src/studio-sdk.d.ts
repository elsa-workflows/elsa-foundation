declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface ElsaStudioModuleApi {
    readonly navigation: { add(item: { id: string; label: string; path: string; order?: number; iconColor?: string }): void };
    readonly routes: { add(item: { id: string; label: string; path: string; component: ComponentType }): void };
    readonly dashboardWidgets: { add(item: { id: string; title: string; order?: number; component: ComponentType }): void };
  }
}
