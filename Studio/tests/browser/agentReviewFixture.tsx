import React from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { HumanReviewPage } from "../../../Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/HumanReviewPage";
import type { HumanReviewView, ReceiptStatus } from "../../../Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/reviewsApi";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/Agent/studio/Elsa.Studio.Agent.Specialists/Client/src/styles.css";

// A durable fake backend: state lives in sessionStorage so a page reload behaves like a server that survived.
const scenario = new URLSearchParams(window.location.search).get("scenario") ?? "default";
const storageKey = `agent-review-fixture:${scenario}`;
const reference = { workflowExecutionId: "run-1", activityExecutionId: "review-1" };
const initial: HumanReviewView = {
  ...reference,
  title: "Launch announcement",
  content: scenario === "json" ? { headline: "Moss gardens", body: "They reward patience.", tags: ["garden", "slow"] } : "Moss gardens reward patience.",
  notes: "Proofreader: ready to publish.",
  contentHash: "hash-1",
  status: scenario === "resolved" ? "Approved" : "Pending",
  decidedBy: scenario === "resolved" ? "grace" : null,
  decisionComment: scenario === "resolved" ? "Looks good." : null
};

type Store = { review: HumanReviewView; requests: { requestId: string; decision: string; comment: string | null }[]; failures: number };
const read = (): Store => JSON.parse(sessionStorage.getItem(storageKey) ?? "null") ?? { review: initial, requests: [], failures: scenario === "unavailable" ? 1 : 0 };
const write = (store: Store) => sessionStorage.setItem(storageKey, JSON.stringify(store));
(window as unknown as { fixtureRequests: () => Store["requests"] }).fixtureRequests = () => read().requests;

const context = {
  http: {
    async getJson(path: string) {
      if (scenario === "missing" || !path.endsWith("/run-1/review-1")) throw Object.assign(new Error("Not found"), { status: 404 });
      return read().review;
    },
    async postJson(_path: string, body: { requestId: string; decision: "Approve" | "Deny"; comment: string | null; contentHash: string }) {
      const store = read();
      store.requests.push({ requestId: body.requestId, decision: body.decision, comment: body.comment });
      let status: ReceiptStatus;
      if (store.failures > 0) { store.failures--; status = "Unavailable"; }
      else if (scenario === "conflict" || body.contentHash !== store.review.contentHash) status = "Conflict";
      else if (store.review.status !== "Pending") status = "AlreadyResolved";
      else {
        store.review = { ...store.review, status: body.decision === "Approve" ? "Approved" : "Rejected", decidedBy: "ada", decisionComment: body.comment, requestId: body.requestId };
        status = "Accepted";
      }
      write(store);
      return { status };
    }
  }
} as unknown as StudioEndpointContext;

createRoot(document.getElementById("root")!).render(
  <ThemeProvider>
    <HumanReviewPage context={context} reference={reference} />
  </ThemeProvider>
);
