import type {
  StudioCodeCompletion,
  StudioCodeCompletionProvider,
  StudioCodeDiagnostic,
  StudioCodeHoverProvider,
  StudioCodeSignatureProvider
} from "./types";

/** Minimal structural shape accepted from a language module or host SDK. */
export interface StudioCodeToolingSymbol {
  id?: string;
  name: string;
  kind?: string;
  documentation?: string;
  shapeId?: string;
  /** Language-projected child symbols that do not require a value-shape lookup. */
  children?: readonly StudioCodeToolingSymbol[];
  sortText?: string;
  signatures?: readonly StudioCodeToolingSignature[];
}

export interface StudioCodeToolingValueShapeMember {
  name: string;
  documentation?: string;
  shapeId: string;
}

export interface StudioCodeToolingValueShape {
  id: string;
  members: readonly StudioCodeToolingValueShapeMember[];
}

export interface StudioCodeToolingSignature {
  label: string;
  documentation?: string;
}

export interface StudioCodeToolingAuthoringContext {
  capabilities?: {
    completion?: boolean;
    hover?: boolean;
    signatures?: boolean;
  };
  target?: StudioCodeToolingSymbol;
  rootSymbols?: readonly StudioCodeToolingSymbol[];
  workflowInputs?: readonly StudioCodeToolingSymbol[];
  visibleVariables?: readonly StudioCodeToolingSymbol[];
  visibleActivityOutputs?: readonly StudioCodeToolingSymbol[];
}

export interface StudioCodeToolingCatalogPage {
  symbols: readonly StudioCodeToolingSymbol[];
  nextCursor?: string;
}

export interface StudioCodeToolingResult<T> {
  state: "ready" | "supported-empty" | "unavailable" | "unauthorized" | "incompatible";
  data?: T;
}

/** Language-owned spelling and member-access rules consumed by the neutral shared editor. */
export interface StudioCodeToolingLanguageProjection {
  projectContext?(context: StudioCodeToolingAuthoringContext | undefined): readonly StudioCodeToolingSymbol[];
  projectCatalog?(
    symbols: readonly StudioCodeToolingSymbol[],
    context: StudioCodeToolingAuthoringContext | undefined
  ): readonly StudioCodeToolingSymbol[];
  memberPathAt?(source: string, position: number, includeCurrentWord: boolean): readonly string[] | undefined;
}

export interface StudioCodeToolingCatalogClient {
  getCatalog?(
    document: StudioCodeToolingDocument,
    authoringContext: StudioCodeToolingAuthoringContext,
    query?: string,
    cursor?: string,
    signal?: AbortSignal
  ): Promise<StudioCodeToolingResult<StudioCodeToolingCatalogPage>>;
  getCompletions?(
    document: StudioCodeToolingDocument,
    authoringContext: StudioCodeToolingAuthoringContext,
    position: { line: number; column: number },
    signal?: AbortSignal
  ): Promise<StudioCodeToolingResult<{ items: readonly StudioCodeToolingCompletionItem[] }>>;
  getHover?(
    document: StudioCodeToolingDocument,
    authoringContext: StudioCodeToolingAuthoringContext,
    position: { line: number; column: number },
    signal?: AbortSignal
  ): Promise<StudioCodeToolingResult<{ contents: string }>>;
  getValueShape?(
    document: StudioCodeToolingDocument,
    authoringContext: StudioCodeToolingAuthoringContext,
    shapeId: string,
    signal?: AbortSignal
  ): Promise<StudioCodeToolingResult<StudioCodeToolingValueShape>>;
}

export interface StudioCodeToolingProjectionOptions {
  document?: StudioCodeToolingDocument;
  authoringContext?: StudioCodeToolingAuthoringContext;
  tooling?: StudioCodeToolingCatalogClient;
  languageProjection?: StudioCodeToolingLanguageProjection;
}

export interface StudioCodeToolingDocument {
  source: string;
  sourceVersion?: number;
}

export interface StudioCodeToolingCompletionItem {
  label: string;
  detail?: string;
  documentation?: string;
  insertText?: string;
  kind?: string;
}

export interface StudioCodeToolingDiagnostic {
  severity?: StudioCodeDiagnostic["severity"];
  code?: string;
  message: string;
  range?: {
    start: { line: number; column: number };
    end: { line: number; column: number };
  };
}

/**
 * Adapts a language-neutral authoring context and optional server catalog to
 * the shared editor. This module intentionally imports no Studio SDK types.
 */
export function createStudioCodeToolingProjection(options: StudioCodeToolingProjectionOptions) {
  let liveSource = options.document?.source;
  let liveSourceVersion = options.document?.sourceVersion ?? 0;
  const currentDocument = <T extends StudioCodeToolingDocument>(document: T, source: string): T => {
    if (source !== liveSource) {
      liveSource = source;
      liveSourceVersion++;
    }
    return { ...document, source, sourceVersion: liveSourceVersion };
  };

  const getSymbols = async (query: string, signal: AbortSignal) => {
    const local = projectContextSymbols(options);
    if (!options.tooling?.getCatalog) return local;

    if (!options.document || !options.authoringContext) return local;
    const result = await options.tooling.getCatalog(
      options.document,
      options.authoringContext,
      query,
      undefined,
      signal
    );
    if (signal.aborted || (result.state !== "ready" && result.state !== "supported-empty")) return local;
    return mergeSymbols(local, projectCatalogSymbols(options, result.data?.symbols ?? []));
  };

  const completionProvider: StudioCodeCompletionProvider = async request => {
    if (options.authoringContext?.capabilities?.completion === false) return [];
    if (options.tooling?.getCompletions && options.document && options.authoringContext) {
      const response = await options.tooling.getCompletions(
        currentDocument(options.document, request.document.value),
        options.authoringContext,
        offsetPosition(request.document.value, request.position),
        request.signal
      );
      if (response.state === "ready" || response.state === "supported-empty") {
        return (response.data?.items ?? []).map(toDirectCompletion);
      }
    }
    const memberSymbols = await resolveMemberSymbols(
      request.document.value,
      request.position,
      options.document,
      options.authoringContext,
      options.tooling,
      request.signal,
      options.languageProjection
    );
    if (memberSymbols) return memberSymbols.map(toCompletion);
    const query = wordAt(request.document.value, request.position).text;
    return (await getSymbols(query, request.signal)).map(toCompletion);
  };

  const hoverProvider: StudioCodeHoverProvider = async (document, position, signal) => {
    if (options.authoringContext?.capabilities?.hover === false) return null;
    if (options.tooling?.getHover && options.document && options.authoringContext) {
      const response = await options.tooling.getHover(
        currentDocument(options.document, document.value),
        options.authoringContext,
        offsetPosition(document.value, position),
        signal
      );
      const contents = response.data?.contents;
      if ((response.state === "ready" || response.state === "supported-empty") && contents) {
        return { documentation: { markdown: contents } };
      }
    }
    const word = wordAt(document.value, position);
    if (!word.text) return null;
    const member = await resolveMemberSymbol(
      document.value,
      position,
      options.document,
      options.authoringContext,
      options.tooling,
      signal,
      options.languageProjection
    );
    const symbol = member ?? (await getSymbols(word.text, signal)).find(candidate => candidate.name === word.text);
    if (!symbol?.documentation) return null;
    return { range: { from: word.from, to: word.to }, documentation: { markdown: symbol.documentation } };
  };

  const signatureProvider: StudioCodeSignatureProvider = async (document, position, signal) => {
    if (options.authoringContext?.capabilities?.signatures === false) return null;
    const word = callableWordAt(document.value, position) ?? wordAt(document.value, position);
    if (!word.text) return null;
    const symbol = (await getSymbols(word.text, signal)).find(candidate => candidate.name === word.text);
    const signature = symbol?.signatures?.[0];
    return signature
      ? { label: signature.label, documentation: signature.documentation ? { markdown: signature.documentation } : undefined }
      : null;
  };

  return { completionProvider, hoverProvider, signatureProvider };
}

/** Associates backend semantic diagnostics with the active editor document. */
export function projectStudioCodeDiagnostics(uri: string, diagnostics: readonly StudioCodeToolingDiagnostic[]): StudioCodeDiagnostic[] {
  return diagnostics.map(diagnostic => ({
    uri,
    severity: diagnostic.severity ?? "info",
    code: diagnostic.code,
    message: diagnostic.message,
    startLineNumber: diagnostic.range?.start.line,
    startColumn: diagnostic.range?.start.column,
    endLineNumber: diagnostic.range?.end.line,
    endColumn: diagnostic.range?.end.column
  }));
}

function projectContextSymbols(options: StudioCodeToolingProjectionOptions) {
  return options.languageProjection?.projectContext?.(options.authoringContext) ?? defaultContextSymbols(options.authoringContext);
}

function projectCatalogSymbols(
  options: StudioCodeToolingProjectionOptions,
  symbols: readonly StudioCodeToolingSymbol[]
) {
  return options.languageProjection?.projectCatalog?.(symbols, options.authoringContext) ?? symbols;
}

function defaultContextSymbols(context?: StudioCodeToolingAuthoringContext) {
  return mergeSymbols(
    context?.target ? [context.target] : [],
    context?.rootSymbols ?? [],
    context?.workflowInputs ?? [],
    context?.visibleVariables ?? [],
    context?.visibleActivityOutputs ?? []
  );
}

async function resolveMemberSymbols(
  source: string,
  position: number,
  document: StudioCodeToolingDocument | undefined,
  context: StudioCodeToolingAuthoringContext | undefined,
  tooling: StudioCodeToolingCatalogClient | undefined,
  signal: AbortSignal,
  languageProjection: StudioCodeToolingLanguageProjection | undefined
): Promise<StudioCodeToolingSymbol[] | null> {
  const path = resolveMemberPath(source, position, false, languageProjection);
  if (!path) return null;
  const resolved = await resolvePath(path.slice(0, -1), document, context, tooling, signal, languageProjection);
  return resolved?.members ?? [];
}

async function resolveMemberSymbol(
  source: string,
  position: number,
  document: StudioCodeToolingDocument | undefined,
  context: StudioCodeToolingAuthoringContext | undefined,
  tooling: StudioCodeToolingCatalogClient | undefined,
  signal: AbortSignal,
  languageProjection: StudioCodeToolingLanguageProjection | undefined
) {
  const path = resolveMemberPath(source, position, true, languageProjection);
  if (!path || path.length < 2) return undefined;
  const memberName = path.at(-1);
  const resolved = await resolvePath(path.slice(0, -1), document, context, tooling, signal, languageProjection);
  return resolved?.members.find(member => member.name === memberName);
}

async function resolvePath(
  segments: readonly string[],
  document: StudioCodeToolingDocument | undefined,
  context: StudioCodeToolingAuthoringContext | undefined,
  tooling: StudioCodeToolingCatalogClient | undefined,
  signal: AbortSignal,
  languageProjection: StudioCodeToolingLanguageProjection | undefined
): Promise<{ members: StudioCodeToolingSymbol[] } | undefined> {
  if (!context || segments.length === 0) return undefined;
  const symbol = projectContextSymbols({ authoringContext: context, languageProjection })
    .find(candidate => candidate.name === segments[0]);
  if (!symbol) return undefined;
  let members = await loadMembers(symbol, document, context, tooling, signal);
  for (const segment of segments.slice(1)) {
    const member = members.find(candidate => candidate.name === segment);
    if (!member) return undefined;
    members = await loadMembers(member, document, context, tooling, signal);
  }
  return { members };
}

function resolveMemberPath(
  source: string,
  position: number,
  includeCurrentWord: boolean,
  languageProjection: StudioCodeToolingLanguageProjection | undefined
) {
  return languageProjection?.memberPathAt?.(source, position, includeCurrentWord)
    ?? memberPathAt(source, position, includeCurrentWord)?.segments;
}

async function loadShapeMembers(
  shapeId: string | undefined,
  document: StudioCodeToolingDocument | undefined,
  context: StudioCodeToolingAuthoringContext,
  tooling: StudioCodeToolingCatalogClient | undefined,
  signal: AbortSignal
) {
  if (!shapeId || !document || !tooling?.getValueShape) return [];
  const response = await tooling.getValueShape(document, context, shapeId, signal);
  if (response.state !== "ready" && response.state !== "supported-empty") return [];
  return (response.data?.members ?? []).map(member => ({
    id: `${shapeId}:${member.name}`,
    name: member.name,
    kind: "property",
    documentation: member.documentation,
    shapeId: member.shapeId
  }));
}

function loadMembers(
  symbol: StudioCodeToolingSymbol,
  document: StudioCodeToolingDocument | undefined,
  context: StudioCodeToolingAuthoringContext,
  tooling: StudioCodeToolingCatalogClient | undefined,
  signal: AbortSignal
) {
  return symbol.children ? Promise.resolve([...symbol.children]) : loadShapeMembers(symbol.shapeId, document, context, tooling, signal);
}

function toDirectCompletion(item: StudioCodeToolingCompletionItem): StudioCodeCompletion {
  return {
    label: item.label,
    detail: item.detail,
    kind: completionKind(item.kind),
    apply: item.insertText,
    documentation: item.documentation ? { markdown: item.documentation } : undefined
  };
}

function mergeSymbols(...groups: readonly (readonly StudioCodeToolingSymbol[])[]) {
  const symbols = new Map<string, StudioCodeToolingSymbol>();
  for (const symbol of groups.flat()) symbols.set(symbol.id ?? symbol.name, symbol);
  return [...symbols.values()];
}

function toCompletion(symbol: StudioCodeToolingSymbol): StudioCodeCompletion {
  const signature = symbol.signatures?.[0];
  return {
    label: symbol.name,
    kind: completionKind(symbol.kind),
    detail: signature?.label,
    documentation: symbol.documentation ? { markdown: symbol.documentation } : undefined,
    boost: sortBoost(symbol.sortText)
  };
}

function completionKind(kind?: string): StudioCodeCompletion["kind"] {
  return kind === "function" || kind === "method" || kind === "property" || kind === "variable" || kind === "keyword" || kind === "filter" || kind === "tag"
    ? kind
    : "value";
}

function sortBoost(sortText?: string) {
  if (!sortText) return undefined;
  const value = Number.parseInt(sortText, 10);
  return Number.isFinite(value) ? -value : undefined;
}

function wordAt(source: string, position: number) {
  const clamped = Math.min(Math.max(0, position), source.length);
  let from = clamped;
  let to = clamped;
  while (from > 0 && /[\w$]/.test(source[from - 1]!)) from--;
  while (to < source.length && /[\w$]/.test(source[to]!)) to++;
  return { from, to, text: source.slice(from, to) };
}

function callableWordAt(source: string, position: number) {
  let depth = 0;
  for (let index = Math.min(Math.max(0, position), source.length) - 1; index >= 0; index--) {
    if (source[index] === ")") {
      depth++;
      continue;
    }
    if (source[index] !== "(") continue;
    if (depth > 0) {
      depth--;
      continue;
    }
    return wordAt(source, index);
  }
  return undefined;
}

function memberPathAt(source: string, position: number, includeCurrentWord = false) {
  const clamped = Math.min(Math.max(0, position), source.length);
  const prefix = source.slice(0, clamped);
  const match = prefix.match(/([\w$]+(?:\.[\w$]*)+)$/);
  if (!match) return undefined;
  const segments = match[1]!.split(".");
  if (!includeCurrentWord && segments.at(-1) !== "") return undefined;
  return { segments };
}

function offsetPosition(source: string, position: number) {
  const clamped = Math.min(Math.max(0, position), source.length);
  const before = source.slice(0, clamped);
  const lines = before.split("\n");
  return { line: lines.length - 1, column: lines.at(-1)?.length ?? 0 };
}
