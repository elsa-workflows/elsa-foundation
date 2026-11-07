import type {
  applyBpmnDocumentCommands,
  applyBpmnDocumentTransaction,
  bpmnNamespaces,
  readBpmnDocument,
  resolveBpmnDocumentReference
} from "./bpmnDocument";
import type { planBpmnDocumentSyntaxChange } from "./bpmnDocumentSyntaxChange";
import type { planLegacyBpmnDocumentMigration, prepareLegacyBpmnDocument } from "./bpmnDocumentMigration";

/** Workflows owns the source contract; optional canvases load it only when needed. */
export interface BpmnDocumentAuthoring {
  readonly bpmnNamespaces: typeof bpmnNamespaces;
  readBpmnDocument: typeof readBpmnDocument;
  applyBpmnDocumentCommands: typeof applyBpmnDocumentCommands;
  applyBpmnDocumentTransaction: typeof applyBpmnDocumentTransaction;
  planBpmnDocumentSyntaxChange: typeof planBpmnDocumentSyntaxChange;
  resolveBpmnDocumentReference: typeof resolveBpmnDocumentReference;
  planLegacyBpmnDocumentMigration: typeof planLegacyBpmnDocumentMigration;
  prepareLegacyBpmnDocument: typeof prepareLegacyBpmnDocument;
}

export async function loadBpmnDocumentAuthoring(): Promise<BpmnDocumentAuthoring> {
  const [document, syntax, migration] = await Promise.all([
    import("./bpmnDocument"), import("./bpmnDocumentSyntaxChange"), import("./bpmnDocumentMigration")
  ]);
  return { ...document, ...migration, planBpmnDocumentSyntaxChange: syntax.planBpmnDocumentSyntaxChange };
}
