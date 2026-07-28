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
    referenceKey?: string | null;
  }

  export interface StudioExpressionDescriptor {
    type: string;
    displayName?: string | null;
    description?: string | null;
    editingMode: "literal" | "text" | "structured" | "reference";
  }

  export type StudioExpressionEditorSurface = "inline" | "expanded";

  export interface StudioExpressionDocument {
    id: string; uri: string; draftId: string; activityId: string; propertyKey: string;
    expressionType: string; source: string; sourceVersion: number; contextVersion?: string;
  }
  export type StudioExpressionToolingState = "ready" | "supported-empty" | "unavailable" | "unauthorized" | "incompatible" | "stale" | "canceled";
  export type StudioExpressionSymbolKind = "value" | "function" | "filter" | "tag" | "namespace" | "member" | "keyword";
  export type StudioExpressionValueShapeKind = "unknown" | "scalar" | "object" | "collection" | "callable";
  export interface StudioExpressionToolingCapabilities { highlighting: boolean; completion: boolean; hover: boolean; signatures: boolean; formatting: boolean; localDiagnostics: boolean; semanticValidation: boolean; }
  export interface StudioExpressionToolingDescriptor { expressionType: string; moduleId: string; moduleVersion: string; contractMinVersion: number; contractMaxVersion: number; capabilities: StudioExpressionToolingCapabilities; catalogVersion?: string; permissionRevision?: string; hostPolicyRevision?: string; }
  export interface StudioExpressionPosition { line: number; column: number; }
  export interface StudioExpressionRange { start: StudioExpressionPosition; end: StudioExpressionPosition; }
  export interface StudioExpressionSignatureParameter { name: string; documentation?: string; shapeId?: string; optional?: boolean; }
  export interface StudioExpressionSignature { label: string; documentation?: string; returnShapeId?: string; parameters: StudioExpressionSignatureParameter[]; }
  export interface StudioExpressionSymbol { id: string; name: string; kind: StudioExpressionSymbolKind; documentation?: string; shapeId?: string; parentId?: string; sortText?: string; signatures?: StudioExpressionSignature[]; }
  export interface StudioExpressionValueShapeMember { name: string; documentation?: string; shapeId: string; }
  export interface StudioExpressionValueShape { id: string; kind: StudioExpressionValueShapeKind; displayName?: string; nullable: boolean; scalarType?: string; elementShapeId?: string; additionalMembers?: boolean; members: StudioExpressionValueShapeMember[]; }
  export interface StudioExpressionAuthoringContext { version: string; catalogVersion?: string; permissionRevision?: string; hostPolicyRevision?: string; capabilities?: StudioExpressionToolingCapabilities; expectedResultType?: string; expectedResultShape?: StudioExpressionValueShape; target?: StudioExpressionSymbol; rootSymbols?: StudioExpressionSymbol[]; workflowInputs: StudioExpressionSymbol[]; visibleVariables: StudioExpressionSymbol[]; visibleActivityOutputs: StudioExpressionSymbol[]; shapeReferences?: string[]; }
  export interface StudioExpressionToolingResult<T> { state: StudioExpressionToolingState; contractVersion: number; expressionType: string; moduleVersion?: string; catalogVersion?: string; contextVersion?: string; data?: T; }
  export interface StudioExpressionSymbolCatalogPage { symbols: StudioExpressionSymbol[]; nextCursor?: string; }
  export interface StudioExpressionValidationDiagnostic { severity: "info" | "warning" | "error"; code?: string; message: string; range?: StudioExpressionRange; documentId: string; sourceVersion: number; contextVersion: string; catalogVersion?: string; }
  export interface StudioExpressionValidationResult { documentId: string; sourceVersion: number; contextVersion: string; diagnostics: StudioExpressionValidationDiagnostic[]; }
  export interface StudioExpressionCompletionItem { label: string; detail?: string; documentation?: string; insertText?: string; kind?: StudioExpressionSymbolKind; }
  export interface StudioExpressionCompletionResult { items: StudioExpressionCompletionItem[]; }
  export interface StudioExpressionHoverResult { contents: string; range?: StudioExpressionRange; }
  export interface StudioExpressionToolingClient {
    describe(signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]>>;
    getCatalog(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, query?: string, cursor?: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionSymbolCatalogPage>>;
    getValueShape(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, shapeId: string, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValueShape>>;
    getAuthoringContext(document: StudioExpressionDocument, state: unknown, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>>;
    getCompletions(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionCompletionResult>>;
    getHover(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, position: StudioExpressionPosition, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionHoverResult>>;
    validate(document: StudioExpressionDocument, authoringContext: StudioExpressionAuthoringContext, signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>>;
    invalidateAuthorization(revisions?: { permissionRevision?: string; hostPolicyRevision?: string }): void;
    revokeAuthorization?(): void;
    restoreAuthorization?(): void;
    dispose(): void;
  }

  export interface StudioExpressionEditorContext {
    syntax: string;
    surface: StudioExpressionEditorSurface;
    descriptor: StudioActivityInputDescriptor;
    activity: unknown;
    expressionDescriptors: StudioExpressionDescriptor[];
    readOnly?: boolean;
    document?: StudioExpressionDocument;
    authoringContext?: StudioExpressionToolingResult<StudioExpressionAuthoringContext>;
    validation?: StudioExpressionToolingResult<StudioExpressionValidationResult>;
    tooling?: StudioExpressionToolingClient;
    editorSessionScope?: string;
    onFocus?(): void;
    onBlur?(): void;
  }

  export interface StudioExpressionEditorProps {
    descriptor: StudioActivityInputDescriptor;
    syntax: string;
    value: unknown;
    disabled?: boolean;
    initialFocus?: boolean;
    context: StudioExpressionEditorContext;
    onChange(value: unknown): void;
    onExpand?(): void;
  }

  export interface StudioExpressionEditorMetadata {
    displayName?: string;
    installHint?: string;
    packageId?: string;
    toolingCapabilities?: Partial<StudioExpressionToolingCapabilities>;
  }

  export interface StudioExpressionEditorContribution {
    id: string;
    order?: number;
    supports(context: StudioExpressionEditorContext): boolean;
    surfaces: Partial<Record<StudioExpressionEditorSurface, ComponentType<StudioExpressionEditorProps>>>;
    createDefaultValue?(context: StudioExpressionEditorContext): unknown;
    metadata?: StudioExpressionEditorMetadata;
    sourceRenderer?: StudioExpressionSourceRenderer;
  }

  export interface StudioExpressionSourceRendererContext {
    expressionType: string;
    value: unknown;
    metadata: Readonly<Record<string, unknown>>;
    isSensitive: boolean;
    surface: "compact" | "expanded";
  }

  export interface StudioExpressionSourceRendererProps {
    context: StudioExpressionSourceRendererContext;
  }

  export interface StudioExpressionSourceRenderer {
    compact: ComponentType<StudioExpressionSourceRendererProps>;
    expanded: ComponentType<StudioExpressionSourceRendererProps>;
  }

  export interface ElsaStudioModuleApi {
    readonly expressionEditors: StudioContributionRegistry<StudioExpressionEditorContribution>;
  }
}
