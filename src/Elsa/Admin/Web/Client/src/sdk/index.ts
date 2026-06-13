import type { ComponentType } from "react";

export type AdminModuleStatus = "available" | "loaded" | "disabled" | "incompatible" | "failed";

export interface AdminModuleManifest {
  id: string;
  displayName: string;
  version: string;
  entry: string;
  styles: string[];
  requiredHostVersion: string;
  requiredSdkVersion: string;
  capabilities: string[];
}

export interface AdminModuleDiagnostic {
  moduleId: string;
  status: AdminModuleStatus | string;
  reason: string;
}

export interface AdminModulesResponse {
  hostVersion: string;
  sdkVersion: string;
  modules: AdminModuleManifest[];
  diagnostics: AdminModuleDiagnostic[];
}

export interface AdminRouteContribution {
  id: string;
  path: string;
  label: string;
  component: ComponentType;
}

export interface AdminNavigationContribution {
  id: string;
  label: string;
  path: string;
  order?: number;
}

export interface AdminDashboardWidgetContribution {
  id: string;
  title: string;
  order?: number;
  component: ComponentType;
}

export interface AdminContributionRegistry<T> {
  add(contribution: T): void;
  list(): T[];
}

export interface ElsaAdminHostContext {
  hostVersion: string;
  sdkVersion: string;
  http: {
    getJson<T>(url: string, init?: RequestInit): Promise<T>;
  };
}

export interface ElsaAdminModuleApi {
  readonly host: ElsaAdminHostContext;
  readonly navigation: AdminContributionRegistry<AdminNavigationContribution>;
  readonly routes: AdminContributionRegistry<AdminRouteContribution>;
  readonly dashboardWidgets: AdminContributionRegistry<AdminDashboardWidgetContribution>;
  readonly panels: AdminContributionRegistry<unknown>;
  readonly toolbarActions: AdminContributionRegistry<unknown>;
  readonly activityEditors: AdminContributionRegistry<unknown>;
  readonly propertyEditors: AdminContributionRegistry<unknown>;
  readonly workflowDesigner: {
    readonly nodeRenderers: AdminContributionRegistry<unknown>;
    readonly toolboxItems: AdminContributionRegistry<unknown>;
  };
  readonly diagnostics: AdminContributionRegistry<AdminModuleDiagnostic>;
}

export interface ElsaAdminModule {
  register(api: ElsaAdminModuleApi): void | Promise<void>;
}

export function createContributionRegistry<T>(): AdminContributionRegistry<T> {
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
