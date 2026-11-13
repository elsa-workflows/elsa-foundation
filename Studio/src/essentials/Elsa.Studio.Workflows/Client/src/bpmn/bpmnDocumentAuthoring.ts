import type * as document from "./bpmnDocument";
import type { planBpmnDocumentSyntaxChange } from "./bpmnDocumentSyntaxChange";
import type { serializeBpmnXml } from "./bpmnDocumentXml";

/** Workflows owns canonical source; optional canvases receive only this lazy authoring port. */
export interface BpmnDocumentAuthoring {
  readonly bpmnNamespaces: typeof document.bpmnNamespaces;
  readBpmnDocument: typeof document.readBpmnDocument;
  applyBpmnDocumentCommands: typeof document.applyBpmnDocumentCommands;
  applyBpmnDocumentTransaction: typeof document.applyBpmnDocumentTransaction;
  resolveBpmnDocumentReference: typeof document.resolveBpmnDocumentReference;
  planBpmnDocumentSyntaxChange: typeof planBpmnDocumentSyntaxChange;
  serializeBpmnXml: typeof serializeBpmnXml;
}

export async function loadBpmnDocumentAuthoring(): Promise<BpmnDocumentAuthoring> {
  const [document, syntax, xml] = await Promise.all([
    import("./bpmnDocument"), import("./bpmnDocumentSyntaxChange"), import("./bpmnDocumentXml")
  ]);
  return { bpmnNamespaces: document.bpmnNamespaces, readBpmnDocument: document.readBpmnDocument,
    applyBpmnDocumentCommands: document.applyBpmnDocumentCommands,
    applyBpmnDocumentTransaction: document.applyBpmnDocumentTransaction,
    resolveBpmnDocumentReference: document.resolveBpmnDocumentReference,
    planBpmnDocumentSyntaxChange: syntax.planBpmnDocumentSyntaxChange, serializeBpmnXml: xml.serializeBpmnXml };
}
