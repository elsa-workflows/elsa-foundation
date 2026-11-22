import React, { useRef, useState } from "react";
import { TableEditor } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/TableEditor";
import { DecisionsPage } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/DecisionsPage";
import type { DecisionAsset, Diagnostic } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionsApi";
import { editTable, newDecisionXml, parseTable } from "../../../extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/decisionXml";
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

const composedXml = (() => {
  let xml = editTable(newDecisionXml(), { kind: "add-clause", output: false });
  const secondInput = parseTable(xml).inputs[1];
  xml = editTable(xml, { kind: "clause", id: secondInput.id, output: false, name: "customerCategory", type: "string" });
  for (let index = 0; index < 3; index++) xml = editTable(xml, { kind: "add-rule" });
  const table = parseTable(xml);
  const conditions = ["<= 21", "> 21", "-", "-"];
  const outcomes = ['"Manual review"', '"Automatic"', '"VIP"', '"Other"'];
  table.rules.forEach((rule, index) => {
    xml = editTable(xml, { kind: "cell", ruleId: rule.id, output: false, column: 0, text: conditions[index] });
    xml = editTable(xml, { kind: "cell", ruleId: rule.id, output: true, column: 0, text: outcomes[index] });
  });
  return xml;
})();
const recoveryTable = parseTable(composedXml);
let recoveryXml = editTable(composedXml, { kind: "cell", ruleId: recoveryTable.rules[0].id, output: false, column: 0, text: "amount <= 21" });
recoveryXml = editTable(recoveryXml, { kind: "cell", ruleId: recoveryTable.rules[1].id, output: false, column: 0, text: "amount > 21" });
const recoveryDiagnostics: Diagnostic[] = recoveryTable.rules.slice(0, 2).map(rule => ({
  code: "UnsupportedExpression",
  message: "This condition is not supported by the selected profile.",
  decisionId: recoveryTable.id,
  ruleId: rule.id,
  clauseId: recoveryTable.inputs[0].id
}));
const composedAsset: DecisionAsset = {
  id: "approval",
  name: "Approval",
  description: "A representative decision for the composed editor check.",
  revision: "revision-1",
  sourceHash: "hash-1",
  xml: composedXml,
  isSensitive: false,
  createdAt: "2026-11-18T10:00:00Z",
  updatedAt: "2026-11-18T10:00:00Z"
};
const composedCase = {
  id: "case-under-age-boy",
  name: "Under age boy",
  revision: "case-revision-1",
  inputs: { amount: { kind: "number", value: "20" }, customerCategory: { kind: "string", value: "Child" } },
  expectedStatus: "Success",
  expectedResult: { kind: "string", value: "Manual review" }
};
const historyRun = { id: "history-run-1", candidateId: "revision-1", candidateHash: "hash-1", passed: false, hasDetailedPayload: false, comparisons: [], evaluatedAt: "2026-11-18T10:00:00Z", completedAt: "2026-11-18T10:00:00Z" };
const recycledAsset: DecisionAsset = { ...composedAsset, revision: "recycled-r1", publishedVersionId: null, deletedAt: "2026-11-20T12:00:00Z" };

export function DecisionPageFixture() {
  const search = new URLSearchParams(window.location.search), page = search.get("page"), initialCollection = page === "collection", recoveryMode = search.get("recovery") === "1";
  const [route, setRoute] = useState<{ assetId?: string; isNew: boolean }>(() => initialCollection ? { isNew: false } : { assetId: composedAsset.id, isNew: false });
  const state = useRef<"recycled" | "active" | "purged">(initialCollection ? "recycled" : "active");
  const currentAsset = useRef<DecisionAsset>(recoveryMode ? { ...composedAsset, xml: recoveryXml, sourceHash: "invalid-hash" } : composedAsset);
  const savedCase = recoveryMode ? { ...composedCase, inputs: { amount: { kind: "number", value: "20" } }, expectedResult: { kind: "string", value: "Approved" } } : composedCase;
  const savedCases = useRef([savedCase]);
  const api = {
    backend: {
      baseUrl: "http://decisions.test/tenant",
      http: {
        getJson: async (path: string) => path.endsWith("/recycle-bin") ? { items: state.current === "recycled" ? [recycledAsset] : [] }
          : path.endsWith("/decisions") ? { items: state.current === "active" ? [currentAsset.current] : [] }
            : path.endsWith("/cases") ? { items: savedCases.current }
              : path.endsWith("/versions") ? { items: [] }
                : path.endsWith("/runs") ? { items: [historyRun] }
                  : path.includes("/runs/") ? historyRun : currentAsset.current,
        postJson: async (path: string, body?: unknown) => {
          if (path.endsWith("/cases")) {
            const created = { ...(body as typeof savedCase), id: "case-created", revision: "case-revision-2" };
            savedCases.current = [...savedCases.current, created];
            return created;
          }
          if (path.endsWith("/test")) {
            if (recoveryMode && currentAsset.current.xml === recoveryXml) throw Object.assign(new Error("Request failed with 400."), { payload: {
              code: "unsupported", message: "The exact candidate cannot be evaluated.", diagnostics: recoveryDiagnostics
            } });
            return { id: "recovery-run", candidateId: currentAsset.current.revision, candidateHash: currentAsset.current.sourceHash, passed: true, hasDetailedPayload: false, comparisons: [], evaluatedAt: "2026-11-18T10:00:00Z", completedAt: "2026-11-18T10:00:00Z" };
          }
          if (path.endsWith("/restore")) state.current = "active";
          if (path.endsWith("/purge")) state.current = "purged";
          if (path.endsWith("/recycle")) state.current = "recycled";
          return { id: composedAsset.id, revision: "revision-2" };
        },
        putJson: async (path: string, body: unknown) => {
          if (path.includes("/cases/")) {
            const id = decodeURIComponent(path.slice(path.lastIndexOf("/") + 1));
            const existing = savedCases.current.find(item => item.id === id);
            if (!existing) throw new Error("Saved case not found.");
            const updated = { ...existing, ...(body as Partial<typeof existing>), revision: "case-revision-2" };
            savedCases.current = savedCases.current.map(item => item.id === id ? updated : item);
            return updated;
          }
          const update = body as Pick<DecisionAsset, "name" | "xml" | "isSensitive"> & Partial<Pick<DecisionAsset, "description">>;
          currentAsset.current = { ...currentAsset.current, ...update, revision: "revision-2", sourceHash: "hash-2", updatedAt: "2026-11-18T11:00:00Z" };
          return currentAsset.current;
        }
      }
    },
    backendSession: {
      capabilities: { capabilities: [{ id: "elsa.api.decisions", contractVersion: "1", links: [{ rel: "decisions", href: "decisions" }] }] },
      identity: { status: "authenticated", subject: "browser-test", tenantId: "tenant", roles: [], permissions: ["decisions:read", "decisions:manage", "decisions:publish", "decisions:test", "decisions:history"] }
    },
    dialogs: { confirm: async () => true, prompt: async () => "Approval", alert: () => undefined },
    notifications: { notify: () => ({ id: "decision-fixture", update: () => undefined, dismiss: () => undefined }), track: <T,>(work: Promise<T>) => work }
  } as unknown as ElsaStudioModuleApi;

  const editor = <DecisionsPage api={api} navigate={path => {
      const target = new URL(path, window.location.href);
      setRoute({ assetId: target.searchParams.get("decision") ?? undefined, isNew: target.searchParams.get("new") === "1" });
    }} assetId={route.assetId} isNew={route.isNew} />;
  return page === "full" ? <div className="browser-decision-shell"><main className="content">{editor}</main></div>
    : <main className="browser-decision-grid" style={{ minHeight: "100vh", padding: "1.5rem", background: "var(--studio-surface)", color: "var(--studio-text)" }}>{editor}</main>;
}