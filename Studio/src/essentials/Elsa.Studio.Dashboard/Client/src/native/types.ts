/** Provider-neutral native Dashboard wire contracts. No provider or saved-author credentials belong here. */
export type ScalarType = "string" | "boolean" | "integer" | "decimal" | "instant" | "date" | "null";
export interface QueryValue { type: ScalarType | string; literal?: string; parameter?: string }
export interface QueryParameter { name: string; type: string; required: boolean; nullable: boolean; default?: QueryValue }
export interface QueryPredicate { field?: string; metric?: string; operator?: string; value?: QueryValue; values?: QueryValue[]; all?: QueryPredicate[]; any?: QueryPredicate[] }
export interface QueryMetric { alias: string; metric?: string; revision?: string; aggregate?: string; field?: string }
export interface QueryTime { field: string; window?: string; from?: string; to?: string; timeZone: string; weekStart: string; bucket?: string }
export interface QuestionQuery {
  schemaVersion: number;
  dataset: { id: string; schemaRevision: string };
  form: "aggregate" | "records";
  parameters: QueryParameter[];
  filters?: QueryPredicate;
  fields: string[];
  groupBy: string[];
  metrics: QueryMetric[];
  time?: QueryTime;
  comparison?: { mode: string; from?: string; to?: string };
  having?: QueryPredicate;
  sort: { reference: string; direction: "ascending" | "descending"; nulls: "first" | "last" }[];
  limit: number;
}
export interface Visualization { kind: "bar" | "line" | "table" | "list" | "metric"; x?: string; y: string[]; label?: string }
export interface QuestionDefinition { query: QuestionQuery; visualization: Visualization; parameterValues?: Record<string, QueryValue> }
export interface DatasetField { id: string; label: string; type: string; filterOperators: string[]; nullable: boolean; groupable: boolean; sortable: boolean; projectable: boolean; unit?: string; description?: string; aggregates?: string[]; businessDimension?: boolean }
export interface DatasetMetric { id: string; revision: string; label: string; unit: string; description: string }
export interface DatasetDeclaration {
  id: string; ownerModuleId: string; schemaRevision: string; title: string; description: string;
  fields: DatasetField[]; metrics: DatasetMetric[];
  availability?: string; unavailableReason?: string;
  time?: { fields: string[]; windows: string[]; buckets: string[]; comparisonModes: string[] };
  forms?: string[]; supportsDrill?: boolean; supportsExport?: boolean; coverageDescription?: string;
}
export interface Page<T> { items: T[]; continuationToken?: string | null }
export interface ResourceCapabilities { canRead: boolean; canEdit: boolean; canShare: boolean; canDelete: boolean; canCopy: boolean }
export interface DashboardTile {
  id: string; questionId?: string; size: "small" | "medium" | "wide" | "full";
  presentation?: Visualization; parameters?: Record<string, QueryValue>;
  referenceState?: "available" | "unavailable"; preserveReference?: boolean;
}
export interface DashboardResource {
  id: string; kind: "question" | "dashboard"; revision: string; name: string; description: string;
  ownerId: string; capabilities: ResourceCapabilities;
  definition?: QuestionDefinition; definitionState?: "available" | "withheld";
  tiles?: DashboardTile[]; createdAt: string; updatedAt: string;
  uses?: { id: string; name: string; tileCount: number }[];
}
export interface QueryProblem { code: string; path: string; message: string }
export interface AudienceGrant { kind: "user" | "role"; id: string; access: "viewer" | "editor" }
export interface AudienceCandidate { kind: "user" | "role"; id: string; name?: string; displayName?: string }
export interface DefinitionRevision { revision: string; name: string; description: string; actorId: string; updatedAt: string }
export interface QueryColumn { id: string; label: string; type: string; unit?: string }
export interface QueryCell { type: string; value: string | null; nullReason?: string }
export interface QueryRow { key: string; values: Record<string, QueryCell>; drill?: unknown }
export interface QueryResult {
  outcome: string; message?: string; interpretation?: string;
  columns: QueryColumn[]; rows: QueryRow[];
  coverage?: { status: string; description: string };
  generatedAt?: string; observedAt?: string; effectiveWindows?: unknown;
  periods?: { role: string; from: string; to: string; timeZone: string }[];
  queryFingerprint?: string; definitionRevision?: string;
  nextCursor?: string;
}

export function initialDefinition(dataset?: DatasetDeclaration): QuestionDefinition {
  return { query: { schemaVersion: 1, dataset: { id: dataset?.id ?? "", schemaRevision: dataset?.schemaRevision ?? "" }, form: "aggregate", parameters: [], fields: [], groupBy: [], metrics: dataset?.metrics[0] ? [{ alias: "value", metric: dataset.metrics[0].id, revision: dataset.metrics[0].revision }] : [], sort: [], limit: 50 }, visualization: { kind: "table", y: [] }, parameterValues: {} };
}

export function serializeTiles(tiles: DashboardTile[]): DashboardTile[] {
  return tiles.map(tile => tile.referenceState === "unavailable" || tile.preserveReference
    ? { id: tile.id, size: tile.size, preserveReference: true }
    : { id: tile.id, size: tile.size, questionId: tile.questionId, presentation: tile.presentation, parameters: tile.parameters });
}

export function resourcePath(kind: DashboardResource["kind"], id?: string) {
  return `/${kind === "question" ? "questions" : "dashboards"}${id ? `/${encodeURIComponent(id)}` : ""}`;
}

export function errorMessage(cause: unknown) { return cause instanceof Error ? cause.message : "The operation could not be completed. Try again."; }
export function accessLost(cause: unknown) { const status = (cause as { status?: number } | null)?.status; return status === 401 || status === 403 || status === 404; }
