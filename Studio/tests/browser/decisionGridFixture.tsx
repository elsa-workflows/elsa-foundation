import React, { useState } from "react";
import { TableEditor } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/TableEditor";
import { DecisionsPage } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionsPage";
import type { DecisionAsset, Diagnostic } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionsApi";
import { editTable, newDecisionXml } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionXml";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";

const initialXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="definitions" name="Approval" namespace="urn:elsa:decisions"><decision id="decision" name="Approval"><decisionTable id="table" hitPolicy="UNIQUE"><input id="amount"><inputExpression id="amount-expression" typeRef="number"><text>amount</text></inputExpression></input><input id="category"><inputExpression id="category-expression" typeRef="string"><text>customerCategory</text></inputExpression></input><output id="result" label="result" typeRef="string"/><rule id="rule_1"><inputEntry id="cell_age_1"><text>&lt;= 40</text></inputEntry><inputEntry id="cell_category_1"><text>"Child"</text></inputEntry><outputEntry id="cell_result_1"><text>"Manual review"</text></outputEntry></rule><rule id="rule_2"><inputEntry id="cell_age_2"><text>&gt; 40</text></inputEntry><inputEntry id="cell_category_2"><text>"Adult"</text></inputEntry><outputEntry id="cell_result_2"><text>"Automatic"</text></outputEntry></rule><rule id="rule_3"><inputEntry id="cell_age_3"><text>-</text></inputEntry><inputEntry id="cell_category_3"><text>"VIP"</text></inputEntry><outputEntry id="cell_result_3"><text>"Fast track"</text></outputEntry></rule></decisionTable></decision></definitions>`;
const diagnostics: Diagnostic[] = [{ code: "ReviewCell", message: "Review the age boundary before publishing.", severity: "warning", ruleId: "rule_1", clauseId: "amount" }];

export function DecisionGridFixture() {
  const [xml, setXml] = useState(initialXml);
  const [error, setError] = useState("");
  return <main className="decisions-page browser-decision-grid" style={{ minHeight: "100vh", padding: "1.5rem", background: "var(--studio-surface)", color: "var(--studio-text)" }}>
    <h1>Decision table grid</h1>
    <TableEditor xml={xml} readOnly={false} diagnostics={diagnostics} onChange={setXml} onError={setError} />
    {error ? <p role="alert">{error}</p> : null}
    <output hidden data-testid="serialized-source" data-source={xml} />
  </main>;
}

const composedXml = editTable(newDecisionXml(), { kind: "add-rule" });
const composedAsset: DecisionAsset = {
  id: "approval",
  name: "Approval",
  description: "A compact, two-rule decision for the composed editor check.",
  revision: "revision-1",
  sourceHash: "hash-1",
  xml: composedXml,
  isSensitive: false,
  createdAt: "2026-11-18T10:00:00Z",
  updatedAt: "2026-11-18T10:00:00Z"
};

export function DecisionPageFixture() {
  const api = {
    backend: {
      baseUrl: "http://decisions.test/tenant",
      http: {
        getJson: async (path: string) => path.endsWith("/versions") ? { items: [] } : composedAsset
      }
    },
    backendSession: {
      capabilities: { capabilities: [{ id: "elsa.api.decisions", contractVersion: "1", links: [{ rel: "decisions", href: "decisions" }] }] },
      identity: { status: "authenticated", subject: "browser-test", tenantId: "tenant", roles: [], permissions: ["decisions:read", "decisions:manage"] }
    },
    dialogs: { confirm: async () => true, prompt: async () => "Approval", alert: () => undefined },
    notifications: { notify: () => ({ id: "decision-fixture", update: () => undefined, dismiss: () => undefined }), track: <T,>(work: Promise<T>) => work }
  } as unknown as ElsaStudioModuleApi;

  return <main className="browser-decision-grid" style={{ minHeight: "100vh", padding: "1.5rem", background: "var(--studio-surface)", color: "var(--studio-text)" }}>
    <DecisionsPage api={api} navigate={() => undefined} assetId={composedAsset.id} />
  </main>;
}