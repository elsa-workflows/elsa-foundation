declare module "@elsa-workflows/studio-sdk" {
  import type { ComponentType } from "react";

  export interface StudioContributionRegistry<T> {
    add(contribution: T): void;
    list(): T[];
  }

  export interface StudioActivityInputDescriptor {
    name: string;
    displayName?: string | null;
    typeName: string;
    uiHint?: string | null;
    defaultSyntax?: string | null;
    isReadOnly?: boolean | null;
  }

  export interface StudioExpressionDescriptor {
    type: string;
    displayName?: string | null;
    description?: string | null;
    editingMode: "literal" | "text" | "structured" | "reference";
  }

  export type StudioExpressionEditorSurface = "inline" | "expanded";

  export interface StudioExpressionEditorContext {
    syntax: string;
    surface: StudioExpressionEditorSurface;
    descriptor: StudioActivityInputDescriptor;
    activity: unknown;
    expressionDescriptors: StudioExpressionDescriptor[];
    readOnly?: boolean;
  }

  export interface StudioExpressionEditorProps {
    descriptor: StudioActivityInputDescriptor;
    syntax: string;
    value: unknown;
    disabled?: boolean;
    initialFocus?: boolean;
    context: StudioExpressionEditorContext;
    onChange(value: unknown): void;
  }

  export interface StudioExpressionEditorContribution {
    id: string;
    order?: number;
    supports(context: StudioExpressionEditorContext): boolean;
    surfaces: Partial<Record<StudioExpressionEditorSurface, ComponentType<StudioExpressionEditorProps>>>;
    createDefaultValue?(context: StudioExpressionEditorContext): unknown;
  }

  export interface ElsaStudioModuleApi {
    readonly expressionEditors: StudioContributionRegistry<StudioExpressionEditorContribution>;
  }
}
