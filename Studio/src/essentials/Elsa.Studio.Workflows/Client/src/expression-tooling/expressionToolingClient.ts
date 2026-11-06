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
  validateExpression,
  type ExpressionToolingOwnerKind
} from "../api/expressionTooling";

const contractVersion = 1;
const wireContractVersion = { major: 1, minor: 0 } as const;
const outcomeStatesV1 = [
  "Success", "SupportedEmpty", "Unavailable", "Unauthorized", "Incompatible", "Stale", "Canceled"
] as const;
const symbolKindsV1 = [
  "Variable", "WorkflowInput", "ActivityResult", "Function", "Filter", "Tag", "Namespace", "Member", "Extension"
] as const;
const valueKindsV1 = ["Unknown", "Scalar", "Object", "Array", "Map", "Function", "Reference"] as const;
const diagnosticSeveritiesV1 = ["Error", "Warning", "Information", "Hint"] as const;
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
  authorizationScope?: string,
  ownerKind: ExpressionToolingOwnerKind = "workflow"
): StudioExpressionToolingClient {
  return new ExpressionToolingClient(context, cacheIdentity, authorizationScope, ownerKind);
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
    private readonly authorizationScope?: string,
    private readonly ownerKind: ExpressionToolingOwnerKind = "workflow"
  ) {
  }

  async describe(signal?: AbortSignal): Promise<StudioExpressionToolingResult<StudioExpressionToolingDescriptor[]>> {
    this.ensureActive();
    if (this.authorizationRevoked) return result("unauthorized", "");
    const generation = this.authorizationGeneration;
    return this.observeAuthorization(await mapToolingRequest(
      "",
      () => getExpressionToolingDescriptors(this.context, signal, this.ownerKind),
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
      document.draftId, document.activityId, document.propertyKey, document.expressionType,
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
        ...locationRequest(document, authoringContext.version, this.ownerKind),
        search: query || undefined,
        skip,
        take: 100
      }, signal, this.ownerKind),
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
    const response = await this.resolveContextPages(document, signal);
    const observed = this.observeAuthorization(response, generation, true);
    if (observed.data) {
      const revisionIdentity = [
        observed.data.version,
        observed.data.catalogVersion ?? "",
        observed.data.permissionRevision ?? "",
        observed.data.hostPolicyRevision ?? ""
      ].join("\u001f");
      const locationIdentity = JSON.stringify([this.ownerKind, document.draftId, document.activityId, document.propertyKey, document.expressionType]);
      const previous = this.contextRevisions.get(locationIdentity);
      if (previous !== undefined && previous !== revisionIdentity) this.metadataCache.clear();
      this.contextRevisions.set(locationIdentity, revisionIdentity);
    }
    return observed;
  }

  private async resolveContextPages(
    document: StudioExpressionDocument,
    signal?: AbortSignal
  ): Promise<StudioExpressionToolingResult<StudioExpressionAuthoringContext>> {
    const pageSize = 500;
    // Persisted sources stop at 5,500 without an explicit truncation marker. At that limit we
    // cannot certify availability; never turn a possibly truncated catalog into "no results".
    const sourceSymbolLimit = 5_500;
    const maximumPages = 12;
    let first: StudioExpressionToolingResult<StudioExpressionAuthoringContext> | undefined;
    const symbols: StudioExpressionSymbol[] = [];
    const shapes = new Map<string, StudioExpressionValueShape>();
    const bindings = new Set<string>();
    for (let page = 0; page < maximumPages; page += 1) {
      const response = await mapToolingRequest(
        document.expressionType,
        () => {
          signal?.throwIfAborted();
          return getExpressionAuthoringContext(this.context, {
            ...locationRequest(document, first?.data?.version, this.ownerKind), skip: page * pageSize, take: pageSize
          }, signal, this.ownerKind);
        },
        value => {
          const parsed = parseContextOutcome(value, document);
          const payload = asRecord(asRecord(asRecord(value)?.result)?.payload);
          const roots = payload?.rootSymbols;
          if (parsed.data && (!matchesDocumentLocation(payload?.document, document, this.ownerKind) ||
              !Array.isArray(roots) || roots.length !== parsed.data.rootSymbols?.length || parsed.contextVersion !== parsed.data.version))
            return result<StudioExpressionAuthoringContext>("incompatible", document.expressionType);
          return parsed;
        }
      );
      if (!response.data || (response.state !== "ready" && response.state !== "supported-empty")) return response;
      const data = response.data as AuthoringContextWithShapes;
      if (first?.data && (data.version !== first.data.version || data.catalogVersion !== first.data.catalogVersion ||
          data.permissionRevision !== first.data.permissionRevision || data.hostPolicyRevision !== first.data.hostPolicyRevision))
        return result<StudioExpressionAuthoringContext>("stale", document.expressionType, { contextVersion: data.version });
      first ??= response;
      const items = data.rootSymbols ?? [];
      if (items.length > pageSize) return result("incompatible", document.expressionType);
      for (const symbol of items) {
        const reference = symbol.parameterBinding;
        if (!reference) continue;
        const identity = JSON.stringify([reference.producerNodeId, reference.projectionKey, reference.isOptional]);
        if (bindings.has(identity)) return result("incompatible", document.expressionType);
        bindings.add(identity);
      }
      symbols.push(...items);
      if (symbols.length >= sourceSymbolLimit) return result<StudioExpressionAuthoringContext>("unavailable", document.expressionType);
      for (const [id, shape] of data[valueShapesKey] ?? []) shapes.set(id, shape);
      if (items.length < pageSize) {
        const complete: AuthoringContextWithShapes = {
          ...first.data!, rootSymbols: symbols,
          workflowInputs: symbols.filter(symbol => symbol.kind === "value" && readWireKind(symbol.id) === "workflowinput"),
          visibleVariables: symbols.filter(symbol => readWireKind(symbol.id) === "variable"),
          visibleActivityOutputs: symbols.filter(symbol => readWireKind(symbol.id) === "activityresult"),
          shapeReferences: [...shapes.keys()], [valueShapesKey]: shapes
        };
        return { ...first, data: complete };
      }
    }
    return result<StudioExpressionAuthoringContext>("unavailable", document.expressionType, { contextVersion: first?.contextVersion });
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
        ...sourceRequest(document, authoringContext.version, this.ownerKind),
        cursor: { line: position.line, character: position.column }
      }, signal, this.ownerKind),
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
        ...sourceRequest(document, authoringContext.version, this.ownerKind),
        position: { line: position.line, character: position.column }
      }, signal, this.ownerKind),
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
      () => validateExpression(this.context, sourceRequest(document, authoringContext.version, this.ownerKind), signal, this.ownerKind),
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
    return JSON.stringify([
      kind,
      this.ownerKind,
      this.cacheIdentity.backend,
      this.cacheIdentity.subject,
      this.cacheIdentity.tenantId,
      ...parts
    ]);
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

function locationRequest(document: StudioExpressionDocument, contextRevision?: string, ownerKind: ExpressionToolingOwnerKind = "workflow") {
  return {
    contractVersion: wireContractVersion,
    ...(ownerKind === "activity-draft" ? { activityDraftId: document.draftId } : { workflowDraftId: document.draftId }),
    nodeId: document.activityId,
    propertyKey: document.propertyKey,
    expressionType: document.expressionType,
    documentRevision: String(document.sourceVersion),
    contextRevision
  };
}

function sourceRequest(document: StudioExpressionDocument, contextRevision?: string, ownerKind: ExpressionToolingOwnerKind = "workflow") {
  return {
    ...locationRequest(document, contextRevision, ownerKind),
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
  const wireKind = wireEnumName(record.kind, symbolKindsV1) ?? "Value";
  const id = `${wireKind}:${readString(record, "symbolId") ?? name}`;
  const parameterBinding = parseActivityResultBinding(record.parameterBinding);
  const referenceIdentity = parameterBinding ? JSON.stringify([parameterBinding.producerNodeId, parameterBinding.projectionKey, parameterBinding.isOptional]) : "";
  const shapeId = asRecord(record.valueShape) ? `symbol:${id}${referenceIdentity ? `:${referenceIdentity}` : ""}` : undefined;
  if (shapeId && shapes) parseValueShape(record.valueShape, shapeId, shapes);
  return {
    id,
    name,
    kind: symbolKind(record.kind),
    documentation: readString(record, "documentation"),
    parameterBinding,
    shapeId,
    signatures: Array.isArray(record.signatures)
      ? record.signatures.map(parseSignature).filter(isDefined)
      : undefined
  };
}

function matchesDocumentLocation(value: unknown, document: StudioExpressionDocument, kind: ExpressionToolingOwnerKind): boolean {
  const location = asRecord(value);
  const owner = asRecord(location?.owner);
  const owns = kind === "activity-draft"
    ? location?.workflowDraftId === "" && owner?.kind === "activity-draft" && owner?.id === document.draftId
    : location?.workflowDraftId === document.draftId && location.owner == null;
  return owns && location?.nodeId === document.activityId && location.propertyKey === document.propertyKey &&
    location.expressionType === document.expressionType && location.documentRevision === String(document.sourceVersion);
}

function parseActivityResultBinding(value: unknown): Record<string, unknown> | undefined {
  const binding = asRecord(value);
  if (readString(binding, "kind")?.toLowerCase() !== "activityresult") return undefined;
  const producerNodeId = readString(binding, "producerNodeId");
  const projectionKey = readString(binding, "projectionKey");
  if (!producerNodeId?.trim() || !projectionKey?.trim() ||
      (binding!.isOptional !== undefined && typeof binding!.isOptional !== "boolean")) return undefined;
  return { kind: "ActivityResult", producerNodeId, projectionKey, isOptional: binding!.isOptional === true };
}

function parseValueShape(
  value: unknown,
  id: string,
  shapes?: Map<string, StudioExpressionValueShape>
): StudioExpressionValueShape | undefined {
  const record = asRecord(value);
  if (!record) return undefined;
  const displayName = readString(record, "displayName");
  const kind = valueShapeKind(record.kind);
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
    kind: symbolKind(record.kind)
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
    severity: diagnosticSeverity(record.severity),
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

function symbolKind(value: unknown): StudioExpressionSymbol["kind"] {
  const name = wireEnumName(value, symbolKindsV1);
  switch (name?.toLowerCase()) {
    case "function": return "function";
    case "filter": return "filter";
    case "tag": return "tag";
    case "namespace": return "namespace";
    case "member": return "member";
    case "extension": return "function";
    default: return "value";
  }
}

function valueShapeKind(value: unknown): StudioExpressionValueShape["kind"] {
  const name = wireEnumName(value, valueKindsV1);
  switch (name?.toLowerCase()) {
    case "scalar": return "scalar";
    case "object":
    case "map":
    case "reference": return "object";
    case "array": return "collection";
    case "function": return "callable";
    default: return "unknown";
  }
}

function diagnosticSeverity(value: unknown): StudioExpressionValidationDiagnostic["severity"] {
  const name = wireEnumName(value, diagnosticSeveritiesV1);
  switch (name?.toLowerCase()) {
    case "error": return "error";
    case "warning": return "warning";
    default: return "info";
  }
}

function parseState(value: unknown): StudioExpressionToolingState | undefined {
  const name = wireEnumName(value, outcomeStatesV1);
  switch (name?.replaceAll("-", "").toLowerCase()) {
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

function wireEnumName(value: unknown, names: readonly string[]): string | undefined {
  if (typeof value === "string") return value;
  return typeof value === "number" && Number.isInteger(value) && value >= 0
    ? names[value]
    : undefined;
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
