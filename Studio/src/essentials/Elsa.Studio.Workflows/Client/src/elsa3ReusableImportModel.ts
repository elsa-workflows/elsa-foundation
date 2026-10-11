import type {
  Elsa3MigrationDiagnostic,
  Elsa3ReusableImportAnalysisPage,
  Elsa3ReusableImportItem
} from "./elsa3ReusableImportTypes";

export interface Elsa3ReusableImportAnalysis {
  collectionHandle: string;
  planId: string;
  processed: number;
  total: number;
  processedDiagnostics: number;
  totalDiagnostics: number;
  nextOffset: number | null;
  items: Elsa3ReusableImportItem[];
  diagnostics: Elsa3MigrationDiagnostic[];
}

export function startElsa3ReusableImportAnalysis(page: Elsa3ReusableImportAnalysisPage): Elsa3ReusableImportAnalysis {
  return {
    collectionHandle: page.collectionHandle,
    planId: page.planId,
    processed: page.processed,
    total: page.total,
    processedDiagnostics: page.processedDiagnostics,
    totalDiagnostics: page.totalDiagnostics,
    nextOffset: page.nextOffset ?? null,
    items: orderedItems(page.items),
    diagnostics: page.diagnostics
  };
}

export function appendElsa3ReusableImportAnalysis(
  current: Elsa3ReusableImportAnalysis,
  page: Elsa3ReusableImportAnalysisPage
) {
  if (page.collectionHandle !== current.collectionHandle || page.planId !== current.planId) {
    throw new Error("Analysis page does not belong to the reviewed immutable collection and plan.");
  }
  if (page.offset !== current.nextOffset ||
    page.total !== current.total ||
    page.totalDiagnostics !== current.totalDiagnostics) {
    throw new Error("Analysis page does not continue the reviewed immutable paging snapshot.");
  }
  const bySourceVersion = new Map(current.items.map(item => [item.sourceVersionId, item]));
  page.items.forEach(item => bySourceVersion.set(item.sourceVersionId, item));
  const diagnosticKeys = new Set(current.diagnostics.map(diagnosticIdentity));
  const diagnostics = [...current.diagnostics];
  page.diagnostics.forEach(diagnostic => {
    const key = diagnosticIdentity(diagnostic);
    if (!diagnosticKeys.has(key)) {
      diagnosticKeys.add(key);
      diagnostics.push(diagnostic);
    }
  });
  return {
    ...current,
    processed: page.processed,
    total: page.total,
    processedDiagnostics: page.processedDiagnostics,
    totalDiagnostics: page.totalDiagnostics,
    nextOffset: page.nextOffset ?? null,
    items: orderedItems([...bySourceVersion.values()]),
    diagnostics
  };
}

export function groupElsa3ReusableImportItems(items: Elsa3ReusableImportItem[]) {
  const groups = new Map<string, Elsa3ReusableImportItem[]>();
  items.forEach(item => {
    const group = groups.get(item.sourceDefinitionId) ?? [];
    group.push(item);
    groups.set(item.sourceDefinitionId, group);
  });
  return [...groups.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([sourceDefinitionId, versions]) => ({
      sourceDefinitionId,
      versions: orderedItems(versions)
    }));
}

export function isElsa3ReusableImportItemSelectable(item: Elsa3ReusableImportItem) {
  return item.canApply !== false &&
    !item.diagnostics.some(diagnostic =>
      diagnostic.isError === true ||
      diagnosticSeverity(diagnostic.severity) === "Error" ||
      diagnostic.code === "ELS3-REUSABLE-DEPENDENCY-CYCLE");
}

export function diagnosticSeverity(severity: string | number) {
  if (typeof severity === "number") return ["Info", "Warning", "Error"][severity] ?? `Severity ${severity}`;
  return severity;
}

export function diagnosticPathSegmentKind(kind: string | number) {
  if (typeof kind === "number") {
    return [
      "SourceVersion",
      "Node",
      "DependencySourceDefinition",
      "DependencySourceVersion"
    ][kind] ?? `Path segment ${kind}`;
  }
  return kind;
}

export function importReceiptStatus(status: string | number) {
  if (typeof status === "number") return ["Applied", "AlreadyImported"][status] ?? `Status ${status}`;
  return status;
}

export function importResourceDisposition(disposition: string | number | null | undefined) {
  if (disposition === null || disposition === undefined) return "Not applicable";
  if (typeof disposition === "number") return ["Created", "Reused"][disposition] ?? `Disposition ${disposition}`;
  return disposition;
}

export function summarizeElsa3ReusableImportSelection(
  items: Elsa3ReusableImportItem[],
  selectedSourceVersionIds: Iterable<string>
) {
  const selected = new Set(selectedSourceVersionIds);
  const included = items.filter(item => selected.has(item.sourceVersionId));
  const reusable = included.filter(item => item.isReusable);
  return {
    exactSourceVersions: included.length,
    activityDefinitions: new Set(reusable.map(item => item.activityDefinitionId ?? item.sourceDefinitionId)).size,
    activityDefinitionVersions: new Set(reusable.map(item => item.activityDefinitionVersionId ?? item.sourceVersionId)).size,
    wrapperWorkflows: reusable.length,
    ordinaryWorkflows: included.filter(item => !item.isReusable).length,
    rewrites: included.reduce((total, item) => total + item.rewrites.length, 0)
  };
}

function orderedItems(items: Elsa3ReusableImportItem[]) {
  return [...items].sort((left, right) =>
    left.sourceDefinitionId.localeCompare(right.sourceDefinitionId) ||
    left.sourceVersion - right.sourceVersion ||
    left.sourceVersionId.localeCompare(right.sourceVersionId));
}

function diagnosticIdentity(diagnostic: Elsa3MigrationDiagnostic) {
  return [
    diagnostic.code,
    diagnostic.path ?? "",
    diagnostic.message,
    diagnostic.cycle.join(">"),
    diagnostic.pathSegments
      .map(segment => `${segment.kind}:${segment.identity}:${segment.location ?? ""}`)
      .join(">"),
    Object.entries(diagnostic.metadata)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, value]) => `${key}:${value}`)
      .join(">")
  ].join("|");
}
