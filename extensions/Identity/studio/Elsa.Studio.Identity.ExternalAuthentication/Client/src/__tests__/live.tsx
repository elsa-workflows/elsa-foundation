import React from "react";
import { createRoot } from "react-dom/client";
import { createHttpClient, resolveBackendPath, type StudioEndpointContext, type StudioRequestInit } from "@elsa-workflows/studio-sdk";
import "@elsa-workflows/studio-ui/tokens.css";
import "@elsa-workflows/studio-ui/styles.css";
// Qualification uses the real host's Classic light defaults, rather than a separate fixture palette.
// Production module code consumes only the public UI/token contract.
import "../../../../../../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../styles.css";
import { ExternalAuthenticationPage } from "../ExternalAuthenticationPage";
import { UserAccessPage } from "../UserAccessPage";

// Actual HTTP qualification entry. It supplies the public SDK transport, never fake domain responses.
const backend = window.location.origin + "/backend";
const tokenKey = "elsa-owned-probe-first-party-access";
async function start() {
  let token = window.sessionStorage.getItem(tokenKey);
  if (!token) {
    const response = await fetch(backend + "/_elsa/identity/token", { credentials: "include" });
    if (!response.ok) throw new Error("Sign in to the owned backend before opening this qualification page.");
    const pair = await response.json() as { accessToken: string };
    token = pair.accessToken; window.sessionStorage.setItem(tokenKey, token);
  }
  const raw = createHttpClient(backend, { defaultHeaders: { Authorization: `Bearer ${token}` },
    fetch: (input, init) => fetch(input, { ...init, credentials: "include" }) });
  const context: StudioEndpointContext = { baseUrl: backend, http: { ...raw,
    getJson: <T,>(path: string, init?: StudioRequestInit) => raw.getJson<T>(resolveBackendPath(backend, path), init),
    postJson: <T,>(path: string, body: unknown, init?: StudioRequestInit) => raw.postJson<T>(resolveBackendPath(backend, path), body, init)
  } };
  const Page = new URL(window.location.href).searchParams.get("view") === "access" ? UserAccessPage : ExternalAuthenticationPage;
  createRoot(document.getElementById("root")!).render(<Page context={context} dialogs={{
    confirm: async options => window.confirm(`${options.title}\n\n${options.message}`),
    alert: async options => { window.alert(options.message); }, prompt: async options => window.prompt(options.message)
  }} />);
}
void start().catch(() => { document.getElementById("root")!.textContent = "Sign in to the owned qualification backend, then reload this page."; });
