import type { ComponentType } from "react";

export type StudioModuleStatus = "available" | "loaded" | "disabled" | "incompatible" | "failed";

export interface StudioModuleManifest {
  id: string;
  displayName: string;
  version: string;
  entry: string;
  styles: string[];
  requiredHostVersion: string;
  requiredSdkVersion: string;
  capabilities: string[];
}

export interface StudioModuleDiagnostic {
  moduleId: string;
  status: StudioModuleStatus | string;
  reason: string;
}

export interface StudioModulesResponse {
  hostVersion: string;
  sdkVersion: string;
  modules: StudioModuleManifest[];
  diagnostics: StudioModuleDiagnostic[];
}

export interface StudioRouteContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType;
}

export interface StudioNavigationContribution {
  id: string;
  label: string;
  path: string;
  order?: number;
}

export interface StudioDashboardWidgetContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export interface StudioPanelContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export interface StudioContributionRegistry<T> {
  add(contribution: T): void;
  list(): T[];
}

export interface ElsaStudioHostContext {
  hostVersion: string;
  sdkVersion: string;
  http: {
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
  };
}

export interface ElsaStudioModuleApi {
  readonly host: ElsaStudioHostContext;
  readonly navigation: StudioContributionRegistry<StudioNavigationContribution>;
  readonly routes: StudioContributionRegistry<StudioRouteContribution>;
  readonly dashboardWidgets: StudioContributionRegistry<StudioDashboardWidgetContribution>;
  readonly panels: StudioContributionRegistry<StudioPanelContribution>;
  readonly toolbarActions: StudioContributionRegistry<unknown>;
  readonly activityEditors: StudioContributionRegistry<unknown>;
  readonly propertyEditors: StudioContributionRegistry<unknown>;
  readonly workflowDesigner: {
    readonly nodeRenderers: StudioContributionRegistry<unknown>;
    readonly toolboxItems: StudioContributionRegistry<unknown>;
  };
  readonly diagnostics: StudioContributionRegistry<StudioModuleDiagnostic>;
}

export interface ElsaStudioModule {
  register(api: ElsaStudioModuleApi): void | Promise<void>;
}

export function createContributionRegistry<T>(): StudioContributionRegistry<T> {
  const contributions: T[] = [];

  return {
    add(contribution) {
      contributions.push(contribution);
    },
    list() {
      return [...contributions];
    }
  };
}

