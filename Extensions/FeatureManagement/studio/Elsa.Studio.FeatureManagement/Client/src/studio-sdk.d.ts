declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioSettingOptionDescriptor {
    label: string;
    value: unknown;
    description?: string | null;
  }

  export interface StudioSettingDescriptor {
    name: string;
    displayName: string;
    description?: string | null;
    category?: string | null;
    group?: string | null;
    clrType?: string | null;
    jsonType?: string | null;
    required: boolean;
    defaultValue?: unknown;
    secret: boolean;
    sensitive: boolean;
    restartRequired: boolean;
    advanced: boolean;
    experimental: boolean;
    uiHint?: string | null;
    optionsProvider?: string | null;
    options: StudioSettingOptionDescriptor[];
  }

  export interface StudioSettingEditorProps {
    setting: StudioSettingDescriptor;
    value: unknown;
    disabled?: boolean;
    onChange(value: unknown): void;
  }

  export interface StudioSettingEditorContribution {
    id: string;
    order?: number;
    supports(setting: StudioSettingDescriptor): boolean;
    component: ComponentType<StudioSettingEditorProps>;
  }

  export interface ElsaStudioModuleApi {
    readonly host: {
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
        postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
      };
    };
    readonly backend: {
      readonly http: {
        getJson<T>(url: string, init?: RequestInit): Promise<T>;
        postJson<T>(url: string, body: unknown, init?: RequestInit): Promise<T>;
      };
    };
    readonly navigation: { add(item: { id: string; label: string; path: string; order?: number }): void };
    readonly routes: { add(item: { id: string; label: string; path: string; component: ComponentType }): void };
    readonly settingEditors: { add(item: StudioSettingEditorContribution): void; list(): StudioSettingEditorContribution[] };
  }
}
