import {
  expressionToolingAuthorizationRevokedEvent,
  StudioHttpError,
  type StudioEndpointContext,
  type StudioExpressionAuthoringContext,
  type StudioExpressionCompletionItem,
  type StudioExpressionCompletionResult,
  type StudioExpressionDocument,
  type StudioExpressionHoverResult,
  type StudioExpressionPosition,
  type StudioExpressionRange,
  type StudioExpressionSignature,
  type StudioExpressionSymbol,
  type StudioExpressionSymbolCatalogPage,
  type StudioExpressionToolingClient,
  type StudioExpressionToolingDescriptor,
  type StudioExpressionToolingResult,
  type StudioExpressionToolingState,
  type StudioExpressionValidationDiagnostic,
  type StudioExpressionValidationResult,
  type StudioExpressionValueShape
} from "@elsa-workflows/studio-sdk";
import {
  ApiCapabilityUnavailableError,
  ApiCapabilityVersionMismatchError
} from "../api/capabilities";
import {
  getExpressionToolingDescriptors,
  getExpressionAuthoringContext,
  getExpressionCompletions,
  getExpressionHover,
  searchExpressionSymbols,
  validateExpression
} from "../api/expressionTooling";

const contractVersion = 1;
const wireContractVersion = { major: 1, minor: 0 } as const;
const valueShapesKey = Symbol("expression-value-shapes");

type AuthoringContextWithShapes = StudioExpressionAuthoringContext & {
  [valueShapesKey]?: ReadonlyMap<string, StudioExpressionValueShape>;
};

export interface ExpressionToolingCacheIdentity {
  backend: string;
  subject: string;
  tenantId: string;
}

/**
 * Creates a workflow-lifetime tooling client. Only permission-scoped symbol metadata is cached;
 * expression sources, hover, completion, and validation requests are never retained.
 */
export function createExpressionToolingClient(
  context: StudioEndpointContext,
  cacheIdentity: ExpressionToolingCacheIdentity,
  authorizationScope?: string
): StudioExpressionToolingClient {
  return new ExpressionToolingClient(context, cacheIdentity, authorizationScope);
}

class ExpressionToolingClient implements StudioExpressionToolingClient {
  private readonly metadataCache = new Map<string, StudioExpressionToolingResult<StudioExpressionSymbolCatalogPage>>();
  private readonly contextRevisions = new Map<string, string>();
  private authorizationRevoked = false;
  private authorizationPendingRestore = false;
  private authorizationGeneration = 0;
  private disposed = false;

  constructor(
    private readonly context: StudioEndpointContext,
    private readonly cacheIdentity: ExpressionToolingCacheIdentity,
    private readonly authorizationScope?: string
  ) {
  }

  async describe(signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]>> {
    this.ensureActive();
    if (this.authorizationRevoked) return result("unauthorized", "");
    const generation = this.authorizationGeneration;
    return this.observeAuthorization(await mapToolingRequest(
      "",
      () => getExpressionToolingDescriptors(this.context, signal),
      parseDescriptorsOutcome
    ), generation);
  }

  async getCatalog(
    document: StudioExpressionDocument,
    authoringContext: StudioExpressionAuthoringContext,
    query?: string,
    cursor?: string,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionSymbolCatalogPage>> {
    this.ensureActive();
    const expressionType = document.expressionType;
    if (this.authorizationRevoked || this.authorizationPendingRestore) return result("unauthorized", expressionType);
    const generation = this.authorizationGeneration;
    const skip = parseCursor(cursor);
    const key = this.cacheKey(
      "symbols",
      document.id,
      String(document.sourceVersion),
      authoringContext.version,
      authoringContext.catalogVersion ?? "",
      authoringContext.permissionRevision ?? "",
      authoringContext.hostPolicyRevision ?? "",
      query ?? "",
      String(skip)
    );
    const cached = this.metadataCache.get(key);
    if (cached) return cached;

    const response = await mapToolingRequest(
      expressionType,
      () => searchExpressionSymbols(this.context, {
        ...locationRequest(document, authoringContext.version),
        search: query || undefined,
        skip,
        take: 100
      }, signal),
      value => parseItemsOutcome(value, expressionType, document, authoringContext.version)
    );
    const catalog = response.data
      ? {
          ...withoutData(response),
          data: {
            symbols: response.data.items.map(completionItemToSymbol),
            nextCursor: response.data.items.length === 100 ? String(skip + 100) : undefined
          }
        }
      : withoutData(response);
    const observed = this.observeAuthorization(catalog, generation);
    if (observed.state === "ready" || observed.state === "supported-empty") this.metadataCache.set(key, observed);
    return observed;
  }

  async getValueShape(
    document: StudioExpressionDocument,
    authoringContext: StudioExpressionAuthoringContext,
    shapeId: string,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionValueShape>> {
    this.ensureActive();
    if (this.authorizationRevoked || this.authorizationPendingRestore) return result("unauthorized", document.expressionType);
    signal?.throwIfAborted();
    const shape = (authoringContext as AuthoringContextWithShapes)[valueShapesKey]?.get(shapeId);
    return shape
      ? result("ready", document.expressionType, {
          contextVersion: authoringContext.version,
          data: shape
        })
      : result("unavailable", document.expressionType);
  }

  async getAuthoringContext(
    document: StudioExpressionDocument,
    _state: unknown,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>> {
    this.ensureActive();
    if (this.authorizationRevoked) return result("unauthorized", document.expressionType);
    const generation = this.authorizationGeneration;
    const response = await mapToolingRequest(
      document.expressionType,
      () => getExpressionAuthoringContext(this.context, locationRequest(document), signal),
      value => parseContextOutcome(value, document)
    );
    const observed = this.observeAuthorization(response, generation, true);
    if (observed.data) {
      const revisionIdentity = [
        observed.data.version,
        observed.data.catalogVersion ?? "",
        observed.data.permissionRevision ?? "",
        observed.data.hostPolicyRevision ?? ""
      ].join("\u001f");
      const previous = this.contextRevisions.get(document.id);
      if (previous !== undefined && previous !== revisionIdentity) this.metadataCache.clear();
      this.contextRevisions.set(document.id, revisionIdentity);
    }
    return observed;
  }

  async getCompletions(
    document: StudioExpressionDocument,
    authoringContext: StudioExpressionAuthoringContext,
    position: StudioExpressionPosition,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionCompletionResult>> {
    this.ensureActive();
    if (this.authorizationRevoked || this.authorizationPendingRestore) return result("unauthorized", document.expressionType);
    const generation = this.authorizationGeneration;
    return this.observeAuthorization(await mapToolingRequest(
      document.expressionType,
      () => getExpressionCompletions(this.context, {
        ...sourceRequest(document, authoringContext.version),
        cursor: { line: position.line, character: position.column }
      }, signal),
      value => parseItemsOutcome(value, document.expressionType, document, authoringContext.version)
    ), generation);
  }

  async getHover(
    document: StudioExpressionDocument,
    authoringContext: StudioExpressionAuthoringContext,
    position: StudioExpressionPosition,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionHoverResult>> {
    this.ensureActive();
    if (this.authorizationRevoked || this.authorizationPendingRestore) return result("unauthorized", document.expressionType);
    const generation = this.authorizationGeneration;
    return this.observeAuthorization(await mapToolingRequest(
      document.expressionType,
      () => getExpressionHover(this.context, {
        ...sourceRequest(document, authoringContext.version),
        position: { line: position.line, character: position.column }
      }, signal),
      value => parseHoverOutcome(value, document, authoringContext.version)
    ), generation);
  }

  async validate(
    document: StudioExpressionDocument,
    authoringContext: StudioExpressionAuthoringContext,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionValidationResult>> {
    this.ensureActive();
    if (this.authorizationRevoked || this.authorizationPendingRestore) return result("unauthorized", document.expressionType);
    const generation = this.authorizationGeneration;
    return this.observeAuthorization(await mapToolingRequest(
      document.expressionType,
      () => validateExpression(this.context, sourceRequest(document, authoringContext.version), signal),
      value => parseValidationOutcome(value, document, authoringContext.version)
    ), generation);
  }

  invalidateAuthorization(): void {
    this.metadataCache.clear();
    this.contextRevisions.clear();
  }

  revokeAuthorization(): void {
    if (this.authorizationRevoked) return;
    this.authorizationRevoked = true;
    this.authorizationPendingRestore = false;
    this.authorizationGeneration++;
    this.invalidateAuthorization();
    this.dispatchAuthorizationEvent(expressionToolingAuthorizationRevokedEvent);
  }

  restoreAuthorization(): void {
    this.ensureActive();
    this.authorizationRevoked = false;
    this.authorizationPendingRestore = true;
    this.authorizationGeneration++;
    this.invalidateAuthorization();
    this.dispatchAuthorizationEvent(expressionToolingAuthorizationRevokedEvent);
  }

  dispose(): void {
    this.invalidateAuthorization();
    this.disposed = true;
  }

  private cacheKey(kind: string, ...parts: string[]) {
    return [
      kind,
      this.cacheIdentity.backend,
      this.cacheIdentity.subject,
      this.cacheIdentity.tenantId,
      ...parts
    ].join("\u001f");
  }

  private ensureActive() {
    if (this.disposed) throw new Error("Expression tooling client has been disposed.");
  }

  private observeAuthorization<T>(
    response: StudioExpressionToolingResult<T>,
    generation: number,
    confirmsAuthorization = false
  ) {
    if (generation !== this.authorizationGeneration)
      return result<T>("unauthorized", response.expressionType);
    if (this.authorizationRevoked) return result<T>("unauthorized", response.expressionType);
    if (response.state === "unauthorized") {
      this.revokeAuthorization();
      return response;
    }
    if (confirmsAuthorization && response.data !== undefined && this.authorizationPendingRestore &&
        (response.state === "ready" || response.state === "supported-empty")) {
      this.authorizationPendingRestore = false;
    }
    return response;
  }

  private dispatchAuthorizationEvent(type: string) {
    if (typeof window === "undefined") return;
    window.dispatchEvent(new CustomEvent(type, {
      detail: { scope: this.authorizationScope }
    }));
  }
}

function parseDescriptorsOutcome(value: unknown): StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]> {
  const outcome = parseOutcome(value, "");
  if (!outcome.ok) return outcome.result;
  const descriptors = Array.isArray(outcome.payload)
    ? outcome.payload.map(parseDescriptor).filter(isDefined)
    : [];
  return result(descriptors.length === 0 ? "supported-empty" : outcome.state, "", {
    data: descriptors,
    contextVersion: outcome.contextVersion
  });
}

function parseDescriptor(value: unknown): StudioExpressionToolingDescriptor | undefined {
  const record = asRecord(value);
  const expressionType = readString(record, "expressionType");
  const moduleId = readString(record, "moduleId");
  const moduleVersion = readString(record, "moduleVersion");
  const version = asRecord(record?.contractVersion);
  const major = typeof version?.major === "number" ? version.major : undefined;
  if (!record || !expressionType || !moduleId || !moduleVersion || major === undefined)
    return undefined;
  return {
    expressionType,
    moduleId,
    moduleVersion,
    contractMinVersion: major,
    contractMaxVersion: major,
    capabilities: parseCapabilities(record.capabilities) ?? unsupportedCapabilities()
  };
}

function unsupportedCapabilities() {
  return {
    highlighting: false,
    completion: false,
    hover: false,
    signatures: false,
    formatting: false,
    localDiagnostics: false,
    semanticValidation: false
  };
}

function locationRequest(document: StudioExpressionDocument, contextRevision?: string) {
  return {
    contractVersion: wireContractVersion,
    workflowDraftId: document.draftId,
    nodeId: document.activityId,
    propertyKey: document.propertyKey,
    expressionType: document.expressionType,
    documentRevision: String(document.sourceVersion),
    contextRevision
  };
}

function sourceRequest(document: StudioExpressionDocument, contextRevision?: string) {
  return {
    ...locationRequest(document, contextRevision),
    source: document.source
  };
}

async function mapToolingRequest<T>(
  expressionType: string,
  request: () => Promise<unknown>,
  parse: (response: unknown) => StudioExpressionToolingResult<T>
): Promise<StudioExpressionToolingResult<T>> {
  try {
    return parse(await request());
  } catch (error) {
    return mapToolingError(error, expressionType);
  }
}

function mapToolingError<T>(error: unknown, expressionType: string): StudioExpressionToolingResult<T> {
  if (isAbortError(error)) return result("canceled", expressionType);
  if (error instanceof ApiCapabilityUnavailableError) return result("unavailable", expressionType);
  if (error instanceof ApiCapabilityVersionMismatchError) return result("incompatible", expressionType);
  if (error instanceof StudioHttpError) {
    if (error.status === 401 || error.status === 403) return result("unauthorized", expressionType);
    if (error.status === 404 || error.status === 503) return result("unavailable", expressionType);
    if (error.status === 409 || error.status === 422) return result("incompatible", expressionType);
  }
  throw error;
}

function parseContextOutcome(
  value: unknown,
  document: StudioExpressionDocument
): StudioExpressionToolingResult<StudioExpressionAuthoringContext> {
  const expressionType = document.expressionType;
  const outcome = parseOutcome(value, expressionType, String(document.sourceVersion));
  if (!outcome.ok) return outcome.result;
  const payload = asRecord(outcome.payload);
  const version = readString(payload, "contextRevision") ?? outcome.contextVersion;
  if (!payload || !version) return result("incompatible", expressionType);
  const shapes = new Map<string, StudioExpressionValueShape>();
  const rootSymbols = readSymbols(payload.rootSymbols, shapes);
  const expectedResultShape = parseValueShape(payload.expectedResultShape, "expected-result", shapes);
  const data: AuthoringContextWithShapes = {
    version,
    catalogVersion: readString(payload, "symbolCatalogRevision"),
    permissionRevision: readString(payload, "permissionRevision"),
    hostPolicyRevision: readString(payload, "policyFingerprint"),
    capabilities: parseCapabilities(payload.capabilities),
    expectedResultType: readString(payload, "expectedResultType"),
    expectedResultShape,
    rootSymbols,
    workflowInputs: rootSymbols.filter(symbol => symbol.kind === "value" && readWireKind(symbol.id) === "workflowinput"),
    visibleVariables: rootSymbols.filter(symbol => readWireKind(symbol.id) === "variable"),
    visibleActivityOutputs: rootSymbols.filter(symbol => readWireKind(symbol.id) === "activityresult"),
    shapeReferences: [...shapes.keys()],
    [valueShapesKey]: shapes
  };
  return result(outcome.state, expressionType, {
    contextVersion: version,
    data
  });
}

function parseItemsOutcome(
  value: unknown,
  expressionType: string,
  document: StudioExpressionDocument,
  contextVersion: string
): StudioExpressionToolingResult<StudioExpressionCompletionResult> {
  const outcome = parseOutcome(value, expressionType, String(document.sourceVersion), contextVersion);
  if (!outcome.ok) return outcome.result;
  const payload = asRecord(outcome.payload);
  const items = Array.isArray(payload?.items) ? payload.items.map(parseCompletionItem).filter(isDefined) : [];
  return result(items.length === 0 ? "supported-empty" : outcome.state, expressionType, {
    contextVersion: outcome.contextVersion,
    data: { items }
  });
}

function parseHoverOutcome(
  value: unknown,
  document: StudioExpressionDocument,
  contextVersion: string
): StudioExpressionToolingResult<StudioExpressionHoverResult> {
  const expressionType = document.expressionType;
  const outcome = parseOutcome(value, expressionType, String(document.sourceVersion), contextVersion);
  if (!outcome.ok) return outcome.result;
  const payload = asRecord(outcome.payload);
  const contents = readString(payload, "contents") ?? "";
  return result(contents ? outcome.state : "supported-empty", expressionType, {
    contextVersion: outcome.contextVersion,
    data: { contents, range: parseRange(payload?.range, false) }
  });
}

function parseValidationOutcome(
  value: unknown,
  document: StudioExpressionDocument,
  contextVersion: string
): StudioExpressionToolingResult<StudioExpressionValidationResult> {
  const outcome = parseOutcome(
    value,
    document.expressionType,
    String(document.sourceVersion),
    contextVersion
  );
  if (!outcome.ok) return outcome.result;
  const payload = asRecord(outcome.payload);
  const diagnostics = Array.isArray(payload?.diagnostics)
    ? payload.diagnostics.map(item => parseDiagnostic(item, document, contextVersion)).filter(isDefined)
    : [];
  return result(diagnostics.length === 0 ? "supported-empty" : outcome.state, document.expressionType, {
    contextVersion: outcome.contextVersion ?? contextVersion,
    data: { documentId: document.id, sourceVersion: document.sourceVersion, contextVersion, diagnostics }
  });
}

function parseOutcome(
  value: unknown,
  expressionType: string,
  expectedDocumentRevision?: string,
  expectedContextRevision?: string
):
  | { ok: true; state: "ready" | "supported-empty"; payload: unknown; contextVersion?: string; documentRevision?: string }
  | { ok: false; result: StudioExpressionToolingResult<never> } {
  const envelope = asRecord(value);
  const outcome = asRecord(envelope?.result);
  const state = parseState(outcome?.state);
  const major = readContractMajor(outcome?.contractVersion);
  if (!state || major !== contractVersion) return { ok: false, result: result("incompatible", expressionType) };
  const documentRevision = readString(outcome, "documentRevision");
  const contextRevision = readString(outcome, "contextRevision");
  if (state !== "ready" && state !== "supported-empty") {
    return { ok: false, result: result<never>(state, expressionType, { contextVersion: contextRevision }) };
  }
  if ((expectedDocumentRevision !== undefined && documentRevision !== expectedDocumentRevision) ||
      (expectedContextRevision !== undefined && contextRevision !== expectedContextRevision)) {
    return { ok: false, result: result<never>("stale", expressionType, { contextVersion: contextRevision }) };
  }
  return {
    ok: true,
    state,
    payload: outcome?.payload,
    contextVersion: contextRevision,
    documentRevision
  };
}

function parseSymbol(
  value: unknown,
  shapes?: Map<string, StudioExpressionValueShape>
): StudioExpressionSymbol | undefined {
  const record = asRecord(value);
  const name = readString(record, "name");
  if (!record || !name) return undefined;
  const wireKind = readString(record, "kind") ?? "Value";
  const id = `${wireKind}:${readString(record, "symbolId") ?? name}`;
  const shapeId = asRecord(record.valueShape) ? `symbol:${id}` : undefined;
  if (shapeId && shapes) parseValueShape(record.valueShape, shapeId, shapes);
  return {
    id,
    name,
    kind: symbolKind(wireKind),
    documentation: readString(record, "documentation"),
    shapeId,
    signatures: Array.isArray(record.signatures)
      ? record.signatures.map(parseSignature).filter(isDefined)
      : undefined
  };
}

function parseValueShape(
  value: unknown,
  id: string,
  shapes?: Map<string, StudioExpressionValueShape>
): StudioExpressionValueShape | undefined {
  const record = asRecord(value);
  if (!record) return undefined;
  const displayName = readString(record, "displayName");
  const kind = valueShapeKind(readString(record, "kind"));
  const item = asRecord(record.item);
  if (item) parseValueShape(item, `${id}:item`, shapes);
  const members = Array.isArray(record.members)
    ? record.members.map(memberValue => {
        const member = asRecord(memberValue);
        const name = readString(member, "name");
        const shape = asRecord(member?.shape);
        if (!name || !shape) return undefined;
        const memberShapeId = `${id}:member:${name}`;
        parseValueShape(shape, memberShapeId, shapes);
        return {
          name,
          documentation: readString(member, "documentation"),
          shapeId: memberShapeId
        };
      }).filter(isDefined)
    : [];
  const parsed = {
    id,
    kind,
    displayName,
    nullable: record.isNullable !== false,
    scalarType: kind === "scalar" ? displayName : undefined,
    elementShapeId: item ? `${id}:item` : undefined,
    additionalMembers: record.hasLazyChildren === true,
    members
  };
  shapes?.set(id, parsed);
  return parsed;
}

function parseCapabilities(value: unknown): StudioExpressionAuthoringContext["capabilities"] {
  const record = asRecord(value);
  if (!record) return undefined;
  return {
    highlighting: false,
    completion: record.supportsCompletions === true,
    hover: record.supportsHover === true,
    signatures: false,
    formatting: false,
    localDiagnostics: false,
    semanticValidation: record.supportsValidation === true
  };
}

function parseSignature(value: unknown): StudioExpressionSignature | undefined {
  const record = asRecord(value);
  const label = readString(record, "display");
  if (!record || !label) return undefined;
  return {
    label,
    parameters: Array.isArray(record.parameters)
      ? record.parameters.filter((item): item is string => typeof item === "string").map(name => ({ name }))
      : []
  };
}

function parseCompletionItem(value: unknown): StudioExpressionCompletionItem | undefined {
  const record = asRecord(value);
  const label = readString(record, "label");
  if (!record || !label) return undefined;
  return {
    label,
    detail: readString(record, "detail"),
    documentation: readString(record, "documentation"),
    insertText: readString(record, "insertText"),
    kind: symbolKind(readString(record, "kind") ?? "Value")
  };
}

function completionItemToSymbol(item: StudioExpressionCompletionItem): StudioExpressionSymbol {
  return {
    id: `${item.kind ?? "value"}:${item.label}`,
    name: item.label,
    kind: item.kind ?? "value",
    documentation: item.documentation
  };
}

function parseDiagnostic(
  value: unknown,
  document: StudioExpressionDocument,
  contextVersion: string
): StudioExpressionValidationDiagnostic | undefined {
  const record = asRecord(value);
  const message = readString(record, "message");
  const range = parseRange(record?.range, true);
  if (!record || !message) return undefined;
  return {
    severity: diagnosticSeverity(readString(record, "severity")),
    code: readString(record, "code"),
    message,
    range,
    documentId: document.id,
    sourceVersion: document.sourceVersion,
    contextVersion
  };
}

function parseRange(value: unknown, oneBased: boolean): StudioExpressionRange | undefined {
  const record = asRecord(value);
  const start = parsePosition(record?.start, oneBased);
  const end = parsePosition(record?.end, oneBased);
  return start && end ? { start, end } : undefined;
}

function parsePosition(value: unknown, oneBased: boolean): StudioExpressionPosition | undefined {
  const record = asRecord(value);
  const line = readNumber(record, "line");
  const column = readNumber(record, "character") ?? readNumber(record, "column");
  if (line == null || column == null) return undefined;
  const offset = oneBased ? 1 : 0;
  return { line: line + offset, column: column + offset };
}

function readSymbols(value: unknown, shapes?: Map<string, StudioExpressionValueShape>) {
  return Array.isArray(value) ? value.map(item => parseSymbol(item, shapes)).filter(isDefined) : [];
}

function symbolKind(value: string): StudioExpressionSymbol["kind"] {
  switch (value.toLowerCase()) {
    case "function": return "function";
    case "filter": return "filter";
    case "tag": return "tag";
    case "namespace": return "namespace";
    case "member": return "member";
    case "extension": return "function";
    default: return "value";
  }
}

function valueShapeKind(value?: string): StudioExpressionValueShape["kind"] {
  switch (value?.toLowerCase()) {
    case "scalar": return "scalar";
    case "object":
    case "map":
    case "reference": return "object";
    case "array": return "collection";
    case "function": return "callable";
    default: return "unknown";
  }
}

function diagnosticSeverity(value?: string): StudioExpressionValidationDiagnostic["severity"] {
  switch (value?.toLowerCase()) {
    case "error": return "error";
    case "warning": return "warning";
    default: return "info";
  }
}

function parseState(value: unknown): StudioExpressionToolingState | undefined {
  if (typeof value !== "string") return undefined;
  switch (value.replaceAll("-", "").toLowerCase()) {
    case "success": return "ready";
    case "supportedempty": return "supported-empty";
    case "unavailable": return "unavailable";
    case "unauthorized": return "unauthorized";
    case "incompatible": return "incompatible";
    case "stale": return "stale";
    case "canceled": return "canceled";
    default: return undefined;
  }
}

function result<T>(
  state: StudioExpressionToolingState,
  expressionType: string,
  options: Partial<Omit<StudioExpressionToolingResult<T>, "state" | "contractVersion" | "expressionType">> | T = {}
): StudioExpressionToolingResult<T> {
  const metadata = isResultOptions(options) ? options : { data: options };
  return { state, contractVersion, expressionType, ...metadata };
}

function withoutData<T>(value: StudioExpressionToolingResult<T>): StudioExpressionToolingResult<never> {
  const { data: _data, ...metadata } = value;
  return metadata;
}

function isResultOptions<T>(
  value: Partial<Omit<StudioExpressionToolingResult<T>, "state" | "contractVersion" | "expressionType">> | T
): value is Partial<Omit<StudioExpressionToolingResult<T>, "state" | "contractVersion" | "expressionType">> {
  return !!value && typeof value === "object" && !Array.isArray(value) && (
    Object.keys(value).length === 0 ||
    "data" in value ||
    "contextVersion" in value ||
    "catalogVersion" in value ||
    "moduleVersion" in value
  );
}

function readWireKind(id: string) {
  return id.slice(0, id.indexOf(":")).toLowerCase();
}

function parseCursor(value?: string) {
  const parsed = Number.parseInt(value ?? "0", 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : 0;
}

function readContractMajor(value: unknown) {
  if (typeof value === "number") return value;
  const record = asRecord(value);
  return readNumber(record, "major");
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function readString(record: Record<string, unknown> | undefined, key: string) {
  const value = record?.[key];
  return typeof value === "string" ? value : undefined;
}

function readNumber(record: Record<string, unknown> | undefined, key: string) {
  const value = record?.[key];
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function isDefined<T>(value: T | undefined): value is T {
  return value !== undefined;
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}
