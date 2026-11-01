import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "../app/App";
import * as moduleLoader from "../app/loader";
import { createStudioAuthManager, createStudioEndpointContext, StudioAuthBoundary } from "../app/auth/studioAuth";
import { useAuthSession, type AuthProviderManager, type AuthSession, type StudioModulesResponse } from "../sdk";

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
  delete window.__ELSA_STUDIO_RUNTIME__;
});

describe("studio auth mounting", () => {
  it.each([false, true])("waits for initial backend discovery before exposing an editable profile (failure: %s)", async failure => {
    const initialPath = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    localStorage.clear(); sessionStorage.clear(); window.history.replaceState({}, "", "/settings/backends/new?backend=legacy");
    const fetchMock = stubShellFetch(), original = fetchMock.getMockImplementation()!;
    let release!: (response: Response) => void;
    const pending = new Promise<Response>(resolve => { release = resolve; });
    fetchMock.mockImplementation(async input => String(input).endsWith("/capabilities") ? pending : original(input));
    const optional = await import("../../../../Elsa.Studio.Backends/Client/src/module");
    const init = vi.spyOn(moduleLoader,"initializeStudioModules").mockImplementation(async (_manifests,context) => ({instances:[{manifest:{id:"test-backends",displayName:"Backends",version:"1",entry:"",styles:[],requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:[]},instance:optional.initialize({...context,configuration:{...context.configuration,extensions:{backends:{configuration:{},profileApiPath:"/_elsa/studio/backends/profiles"}}}})}],diagnostics:[]}));
    const view = await renderApp();
    try {
      await vi.waitFor(() => expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith("/capabilities"))).toBe(true));
      expect(view.container.querySelector(".backend-form")).toBeNull();
      expect(view.container.querySelector('[aria-label="Connecting to backend"]')).not.toBeNull();
      release(new Response(JSON.stringify(failure ? { message: "Unavailable" } : { capabilities: [] }), { status: failure ? 503 : 200, headers: { "content-type": "application/json" } }));
      await vi.waitFor(() => expect(view.container.querySelector(".backend-form")).not.toBeNull());
      const name = view.container.querySelector<HTMLInputElement>(".backend-form input")!;
      flushSync(() => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(name, "Retained profile draft");
        name.dispatchEvent(new Event("input", { bubbles: true }));
      });
      await flushPromises(); await flushPromises();
      expect(view.container.querySelector<HTMLInputElement>(".backend-form input")?.value).toBe("Retained profile draft");
    } finally {
      release(new Response(JSON.stringify({ capabilities: [] }), { status: 200 }));
      await flushPromises(); await view.unmount(); init.mockRestore(); window.history.replaceState({}, "", initialPath);
    }
  });

  it.each([
    ["unavailable", "/module-policy"], ["unavailable", "/settings/module-policy"],
    ["hidden", "/module-policy"], ["hidden", "/settings/module-policy"]
  ] as const)("honors composed %s policy on %s with a satisfied backend", async (state, path) => {
    const initialPath = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    const component = vi.fn(() => <div>Disallowed route content</div>);
    const loader = vi.spyOn(moduleLoader, "registerStudioModuleInstances").mockImplementation(async (_manifests, api) => {
      const route = { id: path, path, label: "Module policy", component, requiresBackend: [{ id: "elsa.api.runtime", contractVersion: "1" }], availability: { state, reason: "Blocked by module policy" } };
      api.routes.add(route);
      api.navigation.add({ id: "module-policy", path: "/settings/module-policy", label: "Module policy", placement: "settings" });
      return;
    });
    try {
      localStorage.clear();sessionStorage.clear();window.history.replaceState({}, "", path);
      const fetchMock = stubShellFetch(), original = fetchMock.getMockImplementation()!;
      fetchMock.mockImplementation(async input => String(input).endsWith("/capabilities")
        ? new Response(JSON.stringify({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [] }] }), { status: 200, headers: { "content-type": "application/json" } })
        : original(input));
      const view = await renderApp();
      try {
        await vi.waitFor(() => expect(view.container.textContent).toContain(state === "hidden" ? "No Studio route is registered for" : "Blocked by module policy"));
        expect(component).not.toHaveBeenCalled();
      } finally {await view.unmount();}
    } finally {loader.mockRestore();window.history.replaceState({}, "", initialPath);}
  });

  it("renders registry shell contributions and honors their composed availability",async()=>{
    stubShellFetch();
    const register=vi.spyOn(moduleLoader,"registerStudioModuleInstances").mockImplementation(async(_instances,api)=>{
      api.shell!.add({id:"acme.allowed",placement:"notice",component:()=> <span>Acme connection extension</span>});
      api.shell!.add({id:"acme.hidden",placement:"notice",availability:{state:"hidden"},component:()=> <span>Hidden connection extension</span>});
    });
    const view=await renderApp();
    try {await vi.waitFor(()=>expect(view.container.textContent).toContain("Acme connection extension"));expect(view.container.textContent).not.toContain("Hidden connection extension");}
    finally{await view.unmount();register.mockRestore();}
  });

  it("cancels deferred document initialization and disposes the late instance",async()=>{
    const fetch=stubShellFetch(),original=fetch.getMockImplementation()!;
    fetch.mockImplementation(async input=>String(input).includes("/_elsa/studio/modules") ? Response.json({...emptyModules(),modules:[{id:"acme",displayName:"Acme",entry:"/acme.js",version:"1",styles:[],requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:["host-initialization"]}]}) : original(input));
    const originalInitializer=moduleLoader.initializeStudioModules;
    let signal:AbortSignal|undefined,release!:(value:import("../sdk").StudioModuleInstance)=>void;
    const pending=new Promise<import("../sdk").StudioModuleInstance>(resolve=>release=resolve);
    const initialize=vi.spyOn(moduleLoader,"initializeStudioModules").mockImplementation((manifests,context,options)=>{
      signal=context.signal;
      return originalInitializer(manifests,context,{...options,importModule:async()=>({initialize:()=>pending})});
    });
    const dispose=vi.fn();const register=vi.fn();
    const view=await renderApp();
    try {await vi.waitFor(()=>expect(signal).toBeDefined());await view.unmount();expect(signal!.aborted).toBe(true);release({dispose,register});await vi.waitFor(()=>expect(dispose).toHaveBeenCalledOnce());expect(register).not.toHaveBeenCalled();}
    finally{initialize.mockRestore();}
  });

  it("shows recovery when a custom connection controller rejects initialization",async()=>{
    stubShellFetch();
    const snapshot={active:null,loading:true,error:null};
    const initialize=vi.spyOn(moduleLoader,"initializeStudioModules").mockImplementation(async(_manifests,context)=>({instances:[{manifest:{id:"acme",displayName:"Acme",entry:"",styles:[],version:"1",requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:[]},instance:{register:vi.fn(),connection:{hostContext:context.host,hostIdentity:context.identity,subscribe:()=>()=>{},getSnapshot:()=>snapshot,initialize:async()=>{throw new Error("Acme connection failed");},qualifyPath:path=>path}}}],diagnostics:[]}));
    const view=await renderApp();
    try{await vi.waitFor(()=>expect(view.container.textContent).toContain("Acme connection failed"));expect(view.container.textContent).toContain("Return Home");expect(view.container.querySelector('[aria-label="Connecting to backend"]')).toBeNull();}
    finally{await view.unmount();initialize.mockRestore();}
  });

  it.each([false,true])("waits for a prepopulated controller initialization before registration (reject: %s)",async reject=>{
    stubShellFetch();
    const {StudioBackendConnection,anonymousAuthSession}=await import("../sdk");
    const active=new StudioBackendConnection({id:"acme",name:"Acme",baseUrl:"http://backend.example/",environment:"Development",source:"configuration"},null,anonymousAuthSession);
    const snapshot={active,loading:false,error:null};
    let resolve!:()=>void,rejectInit!:(error:Error)=>void;
    const pending=new Promise<void>((yes,no)=>{resolve=yes;rejectInit=no;});
    const initializeConnection=vi.fn(()=>pending);
    const register=vi.fn(),dispose=vi.fn(()=>active.retire());
    const initialize=vi.spyOn(moduleLoader,"initializeStudioModules").mockImplementation(async(_manifests,context)=>({instances:[{manifest:{id:"acme",displayName:"Acme",entry:"",styles:[],version:"1",requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:[]},instance:{register,connection:{hostContext:context.host,hostIdentity:context.identity,subscribe:()=>()=>{},getSnapshot:()=>snapshot,initialize:initializeConnection,qualifyPath:path=>path,dispose}}}],diagnostics:[]}));
    const view=await renderApp();
    try {
      await vi.waitFor(()=>expect(view.container.querySelector('[aria-label="Connecting to backend"]')).not.toBeNull());
      await vi.waitFor(()=>expect(initializeConnection).toHaveBeenCalledOnce());
      expect(register).not.toHaveBeenCalled();
      if(reject){rejectInit(new Error("Deferred Acme failure"));await vi.waitFor(()=>expect(view.container.textContent).toContain("Deferred Acme failure"));expect(register).not.toHaveBeenCalled();}
      else {resolve();await vi.waitFor(()=>expect(register).toHaveBeenCalled());}
    } finally {await view.unmount();expect(dispose).toHaveBeenCalledOnce();initialize.mockRestore();}
  });

  it("always calls controller disposal on teardown with pending writes",async()=>{
    stubShellFetch();
    const dispose=vi.fn();
    const snapshot={active:{pendingWrites:1} as import("../sdk").StudioManagedBackendSession,loading:true,error:null};
    const initialize=vi.spyOn(moduleLoader,"initializeStudioModules").mockImplementation(async(_manifests,context)=>({instances:[{manifest:{id:"acme",displayName:"Acme",entry:"",styles:[],version:"1",requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:[]},instance:{register:vi.fn(),connection:{hostContext:context.host,hostIdentity:context.identity,subscribe:()=>()=>{},getSnapshot:()=>snapshot,initialize:async()=>{},qualifyPath:path=>path,dispose}}}],diagnostics:[]}));
    const view=await renderApp();
    try{await vi.waitFor(()=>expect(view.container.querySelector('[aria-label="Connecting to backend"]')).not.toBeNull());await view.unmount();expect(dispose).toHaveBeenCalledOnce();}finally{initialize.mockRestore();}
  });

  it("boots anonymously and attaches no bearer token when no provider is configured", async () => {
    // No runtime auth config -> anonymous contract: the shell must boot with no login gate.
    const fetchMock = stubShellFetch();
    const { container, unmount } = await renderApp();

    expect(createStudioAuthManager({}, "https://studio.example")).toBeNull();
    await vi.waitFor(()=>expect(container.textContent).toContain("Elsa Studio"));
    expect(container.textContent).not.toContain("Signing in");

    // Every request the shell issued must be unauthenticated.
    for (const call of fetchMock.mock.calls) {
      const headers = new Headers((call[1] as RequestInit | undefined)?.headers);
      expect(headers.get("Authorization")).toBeNull();
    }

    await unmount();
  });

  it("provides an explicit anonymous session when authentication is disabled", () => {
    function SessionProbe() {
      const session = useAuthSession();
      return <span>{session.status}:{session.roles.length}:{session.permissions.length}</span>;
    }

    const container = document.createElement("div");
    const root = createRoot(container);
    flushSync(() => root.render(<StudioAuthBoundary manager={null}><SessionProbe /></StudioAuthBoundary>));

    expect(container.textContent).toBe("anonymous:0:0");
    flushSync(() => root.unmount());
  });

  it("gates the shell behind RequireAuth while the session is anonymous, then reveals it once authenticated", async () => {
    let session: AuthSession = { status: "anonymous", roles: [], permissions: [] };
    const manager = stubManager(() => session);
    manager.login = vi.fn(async () => {
      session = { status: "authenticated", subject: "alice", displayName: "Alice", roles: [], permissions: [] };
    });
    stubShellFetch();

    const { container, flush, unmount } = renderBoundary(manager);

    // The guard shows the fallback (not the shell) and starts the login flow while the session is anonymous.
    expect(container.textContent).toContain("Signing in");
    expect(container.textContent).not.toContain("Dashboard");

    await flush();
    await flushPromises();
    expect(manager.login).toHaveBeenCalledTimes(1);

    // After login resolves to an authenticated session the guarded shell renders.
    expect(container.textContent).toContain("Dashboard");

    await unmount();
  });

  it("shows a retryable error when the authentication backend is unavailable", async () => {
    const manager = stubManager(() => ({ status: "unknown", roles: [], permissions: [] }));
    manager.initialize = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    });
    manager.login = vi.fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(undefined);
    vi.spyOn(console, "error").mockImplementation(() => undefined);

    const { container, flush, unmount } = renderBoundary(manager);

    // The error UI needs two settled rejections (initialize, then the login the anonymous
    // session triggers), each followed by a passive-effect/render cycle whose scheduling
    // races a fixed flush count — poll instead of counting flushes.
    await vi.waitFor(async () => {
      await flush();
      expect(container.textContent).toContain("Unable to sign in");
    });
    expect(container.textContent).toContain("authentication service may be unavailable");
    expect(container.querySelector('[role="alert"]')).not.toBeNull();

    const retryButton = Array.from(container.querySelectorAll("button"))
      .find(button => button.textContent === "Try again");
    expect(retryButton).toBeDefined();

    flushSync(() => retryButton?.click());
    await vi.waitFor(async () => {
      await flush();
      expect(manager.login).toHaveBeenCalledTimes(2);
    });
    expect(container.textContent).toContain("Signing in");

    await unmount();
  });

  it("attaches a bearer token and never sends a management-key header on backend requests (#248)", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    // The authenticated client uses the global fetch; stub it so we can inspect the outgoing request.
    vi.stubGlobal("fetch", fetchMock);
    const context = createStudioEndpointContext("https://foundation.example/", manager);
    await context.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers((fetchMock.mock.calls.at(-1)?.[1] as RequestInit).headers);
    expect(headers.get("Authorization")).toBe("Bearer access-token-1");
    // ADR 0037 / #248: the browser holds no host management key, so no request may carry the header.
    expect(headers.get("X-Elsa-Module-Management-Key")).toBeNull();
  });

  it("fails closed when the authenticated Studio context cannot refresh a missing bearer", async () => {
    const fetchMock = vi.fn(async () => new Response("<html>sign in</html>", { status: 200, headers: { "content-type": "text/html" } }));
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => null);
    manager.refresh = vi.fn(async (): Promise<AuthSession> => ({ status: "anonymous", roles: [], permissions: [] }));
    vi.stubGlobal("fetch", fetchMock);
    const context = createStudioEndpointContext("https://foundation.example/", manager);

    await expect(context.http.getJson("/_elsa/workflows/dashboard/runs")).rejects.toMatchObject({ status: 401 });

    expect(manager.refresh).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("falls back to the plain SDK client (no auth) and sends no management-key header when no provider is configured", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const context = createStudioEndpointContext("https://foundation.example/", null);

    await context.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers((fetchMock.mock.calls[0]?.[1] as RequestInit).headers);
    expect(headers.get("Authorization")).toBeNull();
    // The anonymous path carries no management-key header either (ADR 0037 / #248).
    expect(headers.get("X-Elsa-Module-Management-Key")).toBeNull();
  });

  it("exposes a SignalR access-token factory on the authenticated context and none on the anonymous one", async () => {
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    const authenticated = createStudioEndpointContext("https://foundation.example/", manager);
    expect(authenticated.accessTokenFactory).toBeTypeOf("function");
    await expect(authenticated.accessTokenFactory?.()).resolves.toBe("access-token-1");

    const anonymous = createStudioEndpointContext("https://foundation.example/", null);
    expect(anonymous.accessTokenFactory).toBeUndefined();
  });

  it("uses the manager JWT factory for the host hub credential (no browser management key after #248)", async () => {
    // ADR 0037 / #248: the browser holds no host management key, so the host hub credential is always the manager-based
    // JWT factory (the Studio bridge gate accepts that same backend-issued bearer as ?access_token= on the hub path).
    const manager = stubManager(() => ({ status: "authenticated", roles: [], permissions: [] }));
    manager.getAccessToken = vi.fn(async () => "access-token-1");

    const context = createStudioEndpointContext("https://studio.example/", manager);
    await expect(context.accessTokenFactory?.()).resolves.toBe("access-token-1");
  });

  it("gives the anonymous backend context no hub credential", () => {
    // The anonymous backend context has no user session, so there is no hub credential at all — and never a
    // management key, which the browser no longer holds after ADR 0037 / #248.
    const anonymousBackend = createStudioEndpointContext("https://backend.example/", null);
    expect(anonymousBackend.accessTokenFactory).toBeUndefined();
  });

  it("wires the configured token endpoint into the backend manager", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();
      if (url.endsWith("/_elsa/identity/bootstrap")) {
        return new Response(JSON.stringify({
          ownershipMode: "foundation-owned",
          providers: [{ id: "openiddict", kind: "openiddict", enabled: true, isDefault: true }]
        }), { status: 200 });
      }
      if (url.endsWith("/custom/token")) {
        return new Response(JSON.stringify({ accessToken: "custom-token" }), { status: 200 });
      }
      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    const manager = createStudioAuthManager(
      { auth: { enabled: true, tokenEndpoint: "/custom/token" } },
      "https://foundation.example/"
    );

    await expect(manager?.getAccessToken()).resolves.toBe("custom-token");
  });
});

async function renderApp() {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<App />));
  await flushPromises();
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function renderBoundary(manager: AuthProviderManager) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => {
    root.render(
      <StudioAuthBoundary manager={manager}>
        <div>Dashboard</div>
      </StudioAuthBoundary>
    );
  });

  return {
    container,
    // Let queued microtasks (initialize/login) settle and flush the resulting React state updates.
    flush: async () => {
      await flushPromises();
      flushSync(() => undefined);
    },
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

function stubShellFetch() {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === "string" ? input : input.toString();
    if (url.includes("/_elsa/studio/modules")) {
      return new Response(JSON.stringify(emptyModules()), { status: 200 });
    }
    if (url.includes("/_elsa/module-management/registry")) {
      return new Response(JSON.stringify({ modules: [], diagnostics: [] }), { status: 200 });
    }
    // Backend health probe (origin root) and anything else.
    return new Response(JSON.stringify({ status: "Healthy" }), { status: 200, headers: { "content-type": "application/json" } });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function emptyModules(): StudioModulesResponse {
  return { hostVersion: "1.0.0", sdkVersion: "1.0.0", modules: [], diagnostics: [] };
}

function stubManager(getSession: () => AuthSession): AuthProviderManager {
  return {
    getSession: vi.fn(getSession),
    getCapabilities: vi.fn(async () => ({ ownershipMode: "foundation-owned", providers: [] })),
    initialize: vi.fn(async () => getSession()),
    login: vi.fn(async () => undefined),
    handleCallback: vi.fn(async () => getSession()),
    logout: vi.fn(async () => undefined),
    getAccessToken: vi.fn(async () => null),
    refresh: vi.fn(async () => getSession())
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}
