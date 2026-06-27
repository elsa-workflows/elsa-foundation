export interface StudioCodeDocument {
  uri: string;
  language: string;
  value: string;
}

export type StudioCodeDiagnosticSeverity = "info" | "warning" | "error";

export interface StudioCodeDiagnostic {
  uri?: string;
  severity?: StudioCodeDiagnosticSeverity;
  code?: string;
  message: string;
  startLineNumber?: number;
  startColumn?: number;
  endLineNumber?: number;
  endColumn?: number;
}

export interface StudioCodeLanguageSupport {
  readonly language: string;
}

export interface StudioCodeLanguageAdapter {
  language: string;
  displayName: string;
  loadSupport?(): Promise<StudioCodeLanguageSupport>;
}

export type StudioCodeEditorAction = "save" | "format" | string;

export interface StudioCodeEditorProps {
  document: StudioCodeDocument;
  diagnostics?: StudioCodeDiagnostic[];
  readOnly?: boolean;
  theme?: "studio" | "light" | "dark";
  minHeight?: string;
  ariaLabel: string;
  languageAdapter?: StudioCodeLanguageAdapter;
  onChange(document: StudioCodeDocument): void;
  onAction?(action: StudioCodeEditorAction, document: StudioCodeDocument): void;
}
