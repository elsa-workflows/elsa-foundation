import { afterEach, describe, expect, it, vi } from "vitest";
import { BackendConnections, PreparedBackend, canonicalBackendUrl, validatePersonalProfiles, reloadBackendOnPageRestore } from "../../../../Elsa.Studio.Backends/Client/src/connections";
import {
  evaluateBackendRequirements, getBackendCapabilities, resolveBackendPath, qualifyStudioBackendPath, validateApiCapabilities,
  createEndpointContext, StudioHttpError, studioNavigationRequestedEvent,
  studioBackendCapabilitiesChangedEvent,
  type AuthProviderManager, type AuthSession, type StudioBackendProfile, type StudioEndpointContext
} from "../sdk";
import { createStudioRegistry } from "../app/registry";

const user:AuthSession = {status:"authenticated",subject:"alice",tenantId:"tenant-a",roles:[],permissions:[]};
const a:StudioBackendProfile = {id:"a",name:"Backend A",baseUrl:"https://backend.example/shell-a/",environment:"Development",source:"configuration"};
const b:StudioBackendProfile = {id:"b",name:"Backend B",baseUrl:"https://other.example/shell-b/",environment:"Test",source:"configuration"};
const document = {capabilities:[{id:"elsa.api.runtime",contractVersion:"1",links:[{rel:"workflow-instances",href:"runtime/workflows/instances"}]}]};
const response = (value:unknown,status=200) => new Response(JSON.stringify(value),{status,headers:{"Content-Type":"application/json"}});
function manager(token="token-a"):AuthProviderManager {
  return {getSession:()=>user,getAccessToken:async()=>token,refresh:async()=>user,initialize:async()=>user,getCapabilities:async()=>({ownershipMode:"foundation-owned",providers:[]}),login:async()=>undefined,logout:async()=>undefined,handleCallback:async()=>user};
}
function controller(commit=vi.fn(),identity=user) {
  const host = {...createEndpointContext("https://studio.example/"),http:{...createEndpointContext("https://studio.example/").http,getJson:vi.fn().mockRejectedValue(new StudioHttpError(404,"Not found"))}};
  return {commit,connections:new BackendConnections({profileApiPath:"/_elsa/studio/backends/profiles",backendBaseUrl:a.baseUrl,hostId:"studio-one",backends:{profiles:[a,b]}},host,manager(),identity,commit)};
}
afterEach(() => {vi.unstubAllGlobals();localStorage.clear();sessionStorage.clear();window.history.replaceState({},"","/");});

describe("backend capability contracts",() => {
  it("keeps shell prefixes exactly once and rejects escape paths",() => {
    expect(resolveBackendPath(a.baseUrl,"/modularity/features")).toBe("/shell-a/modularity/features");
    expect(resolveBackendPath(a.baseUrl,"/shell-a/capabilities")).toBe("/shell-a/capabilities");
    for (const path of ["../capabilities","%2e%2e/capabilities","https://other.example/a","//other.example/a","a\\b"])
      expect(()=>resolveBackendPath(a.baseUrl,path)).toThrow();
    expect(canonicalBackendUrl("https://backend.example/shell-a")).toBe(a.baseUrl);
  });
  it.each([null,{}, {capabilities:null}, {capabilities:[{id:"x",contractVersion:"invalid",links:[]}]}, {capabilities:[{id:"x",contractVersion:"1",links:[{rel:"list",href:"https://other.example/"}]}]}])("rejects malformed discovery rather than treating it as an empty backend",value => {
    expect(()=>validateApiCapabilities(value)).toThrow();
  });
  it("does not share a cached document between identities at the same URL",async()=>{
    const first = {baseUrl:a.baseUrl,http:{getJson:vi.fn().mockResolvedValue(document)}} as unknown as StudioEndpointContext;
    const second = {baseUrl:a.baseUrl,http:{getJson:vi.fn().mockResolvedValue({capabilities:[]})}} as unknown as StudioEndpointContext;
    await expect(getBackendCapabilities(first)).resolves.toEqual(document);
    await expect(getBackendCapabilities(second)).resolves.toEqual({capabilities:[]});
    await getBackendCapabilities(first);
    expect(first.http.getJson).toHaveBeenCalledTimes(1);
    expect(second.http.getJson).toHaveBeenCalledTimes(1);
  });
  it("keeps discovery failure, unsupported, incompatible, missing operations and dormant reasons distinct",()=>{
    const requirement=[{id:"elsa.api.runtime",contractVersion:"1",relations:["workflow-instances"]}];
    expect(evaluateBackendRequirements({capabilities:null,discoveryError:"Forbidden"},requirement).reason).toBe("Forbidden");
    expect(evaluateBackendRequirements({capabilities:{capabilities:[]}},requirement).reason).toContain("does not support");
    expect(evaluateBackendRequirements({capabilities:{capabilities:[{...document.capabilities[0],contractVersion:"2"}]}},requirement).reason).toContain("advertises 2");
    expect(evaluateBackendRequirements({capabilities:{capabilities:[{...document.capabilities[0],links:[]}]}},requirement).reason).toContain("workflow-instances");
    expect(evaluateBackendRequirements({capabilities:{capabilities:[{...document.capabilities[0],status:"dormant",reason:"Waiting for schema finalization."}]}},requirement).reason).toBe("Waiting for schema finalization.");
  });
  it("gates navigation and preserves a direct route with its unavailable reason",()=>{
    const session = new PreparedBackend(a,null,{status:"anonymous",roles:[],permissions:[]});session.capabilities={capabilities:[]};
    const api=createStudioRegistry({hostVersion:"1",sdkVersion:"1",...createEndpointContext("https://studio.example/")},{backendContext:session.context,backendSession:session});
    const requirements=[{id:"elsa.api.runtime",contractVersion:"1"}];
    api.navigation.add({id:"runs",label:"Runs",path:"/runs",requiresBackend:requirements});
    api.routes.add({id:"runs",label:"Runs",path:"/runs",component:()=>null,requiresBackend:requirements});
    expect(api.navigation.list()).toEqual([]);
    expect(api.routes.compose({includeUnavailable:true})[0].availability.reason).toContain("does not support");
    session.dispose();
  });
});

describe("immutable backend lifetimes",()=>{
  it("uses a backend's own bearer and token factory, never the Studio-host bearer",async()=>{
    const request=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>response(document));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(b,{...manager("token-b"),getSession:()=>({...user,subject:"bob"})},{...user,subject:"bob"});
    const api=createStudioRegistry({hostVersion:"1",sdkVersion:"1",...createEndpointContext("https://studio.example/"),accessTokenFactory:async()=>"host-token"},{backendContext:selected.context,backendSession:selected});
    await api.backend.http.getJson("/capabilities");
    expect(request.mock.calls[0][0]).toBe("https://other.example/shell-b/capabilities");
    expect(new Headers(request.mock.calls[0][1]?.headers).get("Authorization")).toBe("Bearer token-b");
    await expect(api.backend.accessTokenFactory!()).resolves.toBe("token-b");
    selected.dispose();
  });
  it("rejects a disposed read after token acquisition without issuing a request",async()=>{
    let issueToken!:(value:string)=>void;
    const auth={...manager(),getAccessToken:()=>new Promise<string>(resolve=>{issueToken=resolve;})};
    const request=vi.fn(async()=>response(document));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(a,auth,user);
    const read=selected.context.http.getJson("/capabilities");
    selected.dispose();issueToken("token-a");
    await expect(read).rejects.toMatchObject({name:"AbortError"});expect(request).not.toHaveBeenCalled();
  });
  it("does not dispose an accepted mutation or retry it on another backend",async()=>{
    let finish!:(value:Response)=>void;
    const request=vi.fn((_input:RequestInfo|URL,_init?:RequestInit)=>new Promise<Response>(resolve=>{finish=resolve;}));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(a,null,{status:"anonymous",roles:[],permissions:[]});
    const write=selected.context.http.postJson("/changes",{value:1});
    expect(selected.pendingWrites).toBe(1);
    expect(()=>selected.dispose()).toThrow("Wait for backend changes");
    finish(response({saved:true}));await write;
    expect(selected.pendingWrites).toBe(0);selected.dispose();
    await expect(selected.context.http.postJson("/changes",{})).rejects.toMatchObject({name:"AbortError"});
    expect(request).toHaveBeenCalledTimes(1);
  });
});

describe("backend profile selection",()=>{
  it.each(["elsa.studio.backend.selection.v1", "elsa.studio.backend.sign-in.v1"])("settles root initialization when optional storage read %s is unavailable", async deniedKey => {
    window.history.replaceState({}, "", "/");
    const original = Storage.prototype.getItem;
    const storage = vi.spyOn(Storage.prototype, "getItem").mockImplementation(function(this: Storage, key: string) {
      if (key === deniedKey) throw new DOMException("Storage is blocked", "SecurityError");
      return original.call(this, key);
    });
    vi.stubGlobal("fetch", vi.fn(async () => response(document)));
    const { connections } = controller();
    try {
      await expect(connections.initialize()).resolves.toBeUndefined();
      expect(connections.getSnapshot().loading).toBe(false);
      expect(connections.getSnapshot().active?.profile.id).toBe(a.id);
      expect(connections.getSnapshot().active?.capabilities).toEqual(document);
    } finally { storage.mockRestore(); connections.getSnapshot().active?.dispose(); }
  });

  it("keeps the default connection usable when a sign-in marker cannot be consumed", async () => {
    window.history.replaceState({}, "", "/?backendConnect=b");
    sessionStorage.setItem("elsa.studio.backend.sign-in.v1", JSON.stringify({ id: b.id, baseUrl: b.baseUrl, time: Date.now() }));
    const original = Storage.prototype.removeItem;
    const storage = vi.spyOn(Storage.prototype, "removeItem").mockImplementation(function(this: Storage, key: string) {
      if (key === "elsa.studio.backend.sign-in.v1") throw new DOMException("Storage is blocked", "SecurityError");
      return original.call(this, key);
    });
    const request = vi.fn(async () => response(document)); vi.stubGlobal("fetch", request);
    const { connections, commit } = controller();
    try {
      await expect(connections.initialize()).resolves.toBeUndefined();
      expect(connections.getSnapshot().loading).toBe(false);
      expect(connections.getSnapshot().active?.profile.id).toBe(a.id);
      expect(connections.getSnapshot().error).toContain("Choose the backend again to connect.");
      expect(commit).not.toHaveBeenCalled();
      expect(request).toHaveBeenCalledTimes(1);
    } finally { storage.mockRestore(); connections.getSnapshot().active?.dispose(); }
  });

  it("initializes on HTTP hosts without crypto.randomUUID",async()=>{
    const random=vi.spyOn(crypto,"randomUUID").mockImplementation(()=>{throw new Error("Secure-context API is unavailable");});
    vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
    try {const {connections}=controller();await connections.initialize();expect(connections.getSnapshot().active?.capabilities).toEqual(document);expect(random).not.toHaveBeenCalled();}
    finally {random.mockRestore();}
  });
  it("commits a successful preparation to a backend-qualified safe landing route",async()=>{
    const request=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>response(document));vi.stubGlobal("fetch",request);
    const {connections,commit}=controller();await connections.initialize();await connections.connect(b.id);
    expect(commit).toHaveBeenCalledWith("/?backend=b");
    const targetCalls=request.mock.calls.filter(([input])=>String(input).includes("other.example"));
    expect(targetCalls).toHaveLength(1);expect(new Headers(targetCalls[0][1]?.headers).has("Authorization")).toBe(false);
  });
  it("retains the current backend after failed discovery",async()=>{
    vi.stubGlobal("fetch",vi.fn(async(input:RequestInfo|URL)=>String(input).includes("other.example") ? response({error:"Unavailable"},503) : response(document)));
    const {connections,commit}=controller();await connections.initialize();const current=connections.getSnapshot().active;
    await connections.connect(b.id);expect(commit).not.toHaveBeenCalled();expect(connections.getSnapshot().active).toBe(current);expect(current?.disposed).toBe(false);
  });
  it("checks the draft guard again after candidate preparation",async()=>{
    let finish!:(value:Response)=>void;
    vi.stubGlobal("fetch",vi.fn((input:RequestInfo|URL)=>String(input).includes("other.example") ? new Promise<Response>(resolve=>{finish=resolve;}) : Promise.resolve(response(document))));
    const {connections,commit}=controller();await connections.initialize();const pending=connections.connect(b.id);
    const guard=(event:Event)=>event.preventDefault();window.addEventListener(studioNavigationRequestedEvent,guard);
    try {finish(response(document));await pending;expect(commit).not.toHaveBeenCalled();expect(connections.getSnapshot().active?.disposed).toBe(false);}
    finally {window.removeEventListener(studioNavigationRequestedEvent,guard);}
  });
  it("never interprets an unqualified resource URL using a remembered different backend",async()=>{
    sessionStorage.setItem("elsa.studio.backend.selection.v1","b");window.history.replaceState({},"","/workflows/instances?instance=shared-id");
    vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
    const {connections}=controller();await connections.initialize();expect(connections.getSnapshot().active?.profile.id).toBe("a");
    expect(connections.qualifyPath("/workflows/instances?instance=shared-id")).toBe("/workflows/instances?instance=shared-id&backend=a");
  });
  it("validates personal profile metadata and rejects credentials and source overrides",()=>{
    const personal={id:"mine",name:"My backend",baseUrl:b.baseUrl,environment:"Test"};
    expect(validatePersonalProfiles([personal])[0].source).toBe("personal");
    for (const invalid of [{...personal,baseUrl:"https://user:password@backend.example/"},{...personal,token:"secret"},{...personal,source:"configuration"}]) expect(()=>validatePersonalProfiles([invalid])).toThrow();
    expect(()=>validatePersonalProfiles([personal,personal])).toThrow();
  });
});

describe("backend isolation regressions",() => {
  it("preserves resource links and explicit targets, accepting query paths",() => {
    const current="https://studio.example/workflows/definitions?backend=b";
    expect(qualifyStudioBackendPath("/workflows/instances/shared-id?tab=activity",current)).toBe("/workflows/instances/shared-id?tab=activity&backend=b");
    expect(qualifyStudioBackendPath("/workflows/instances/shared-id?backend=a",current)).toContain("backend=a");
    expect(qualifyStudioBackendPath("https://outside.example/",current)).toBe("https://outside.example/");
    expect(resolveBackendPath(a.baseUrl,"/search?path=a%2Fb")).toBe("/shell-a/search?path=a%2Fb");
    expect(resolveBackendPath(a.baseUrl,a.baseUrl+"capabilities")).toBe("/shell-a/capabilities");
    expect(()=>resolveBackendPath(a.baseUrl,"https://backend.example/outside")).toThrow();
  });
  it("supports prepared HTTP host actions without sending foreign-origin credentials",async()=>{
    const request=vi.fn(async(_input:RequestInfo|URL)=>response({ok:true}));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(a,manager(),user);
    await selected.context.http.requestResponse!("https://backend.example/workflow-trigger?value=a%2Fb",{method:"POST"});
    expect(request.mock.calls[0][0]).toBe("https://backend.example/workflow-trigger?value=a%2Fb");
    await expect(selected.context.http.requestResponse!("https://outside.example/steal",{method:"POST"})).rejects.toThrow("selected backend host");
    expect(request).toHaveBeenCalledTimes(1);selected.dispose();
  });
  it("retires a lifetime if token acquisition changes its tenant",async()=>{
    let current=user;
    const auth={...manager(),getSession:()=>current,getAccessToken:async()=>{current={...user,tenantId:"tenant-b"};return "other-token";}};
    const request=vi.fn(async()=>response(document));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(a,auth,user);
    await expect(selected.context.http.postJson("/modularity/features/apply",{})).rejects.toMatchObject({name:"AbortError"});
    expect(request).not.toHaveBeenCalled();expect(selected.disposed).toBe(true);
  });
  it("closes admission after a previously successful capability refresh fails",async()=>{
    const request=vi.fn().mockResolvedValueOnce(response(document)).mockResolvedValueOnce(response({},403));vi.stubGlobal("fetch",request);
    const selected=new PreparedBackend(a,null,{status:"anonymous",roles:[],permissions:[]});
    await selected.refreshCapabilities();await expect(selected.refreshCapabilities()).rejects.toMatchObject({status:403});
    expect(selected.capabilities).toBeNull();
    expect(evaluateBackendRequirements(selected,[{id:"elsa.api.runtime",contractVersion:"1"}]).state).toBe("unavailable");selected.dispose();
  });
  it("keeps the old lifetime usable if navigation is cancelled or tab storage blocked",async()=>{
    vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
    const {connections,commit}=controller();await connections.initialize();const active=connections.getSnapshot().active!;
    const storage=vi.spyOn(Storage.prototype,"setItem").mockImplementation(()=>{throw new Error("blocked storage");});
    try {await connections.connect(b.id);expect(commit).toHaveBeenCalled();expect(active.disposed).toBe(false);await expect(active.context.http.getJson("/capabilities")).resolves.toEqual(document);}
    finally {storage.mockRestore();}
  });
});

describe("resource navigation lifetime admission",()=>{
  it("rejects an explicit foreign target rather than reinterpreting its resource on the active backend",async()=>{
    vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
    const {connections}=controller();await connections.initialize();
    expect(connections.qualifyPath("/workflows/instances/b-only?backend=b")).toBeNull();
    expect(connections.getSnapshot().error).toContain("another backend");
    expect(connections.getSnapshot().active?.profile.id).toBe("a");
  });
  it("reloads a retired connection restored from the browser cache",async()=>{
    vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
    const {connections}=controller();await connections.initialize();
    const reload=vi.fn();const cleanup=reloadBackendOnPageRestore(connections,reload);
    try {
      window.dispatchEvent(new PageTransitionEvent("pageshow",{persisted:false}));expect(reload).not.toHaveBeenCalled();
      connections.getSnapshot().active!.dispose();
      window.dispatchEvent(new PageTransitionEvent("pageshow",{persisted:false}));expect(reload).not.toHaveBeenCalled();
      window.dispatchEvent(new PageTransitionEvent("pageshow",{persisted:true}));expect(reload).toHaveBeenCalledTimes(1);
    } finally {cleanup();}
  });
});

it("rejects a late old response when both backends use the same resource id",async()=>{
  let finish!:(response:Response)=>void;
  vi.stubGlobal("fetch",vi.fn((input:RequestInfo|URL)=>String(input).includes("other.example") ? Promise.resolve(response({id:"shared-id",name:"Backend B data"})) : new Promise<Response>(resolve=>{finish=resolve;})));
  const first=new PreparedBackend(a,null,{status:"anonymous",roles:[],permissions:[]});
  const second=new PreparedBackend(b,null,{status:"anonymous",roles:[],permissions:[]});
  const old=first.context.http.getJson("/resources/shared-id");first.dispose();
  const current=await second.context.http.getJson("/resources/shared-id");
  finish(response({id:"shared-id",name:"Backend A data"}));
  await expect(old).rejects.toMatchObject({name:"AbortError"});
  expect(current).toEqual({id:"shared-id",name:"Backend B data"});second.dispose();
});

it("notifies the shell when active Test connection clears stale capabilities",async()=>{
  vi.stubGlobal("fetch",vi.fn().mockResolvedValueOnce(response(document)).mockResolvedValue(response({},403)));
  const {connections}=controller();await connections.initialize();
  const changed=vi.fn(), notified=vi.fn();
  const unsubscribe=connections.subscribe(changed);
  window.addEventListener(studioBackendCapabilitiesChangedEvent,notified);
  try {
    await expect(connections.inspect(a.id)).rejects.toMatchObject({status:403});
    expect(changed).toHaveBeenCalled();expect(notified).toHaveBeenCalledOnce();
    expect(connections.getSnapshot().active?.capabilities).toBeNull();
  } finally {unsubscribe();window.removeEventListener(studioBackendCapabilitiesChangedEvent,notified);}
});

it("canonicalizes the tab-restored Home backend before resource links are rendered",async()=>{
  sessionStorage.setItem("elsa.studio.backend.selection.v1",b.id);
  window.history.replaceState({},"","/?filter=mine#overview");
  vi.stubGlobal("fetch",vi.fn(async()=>response(document)));
  const {connections}=controller();await connections.initialize();
  expect(connections.getSnapshot().active?.profile.id).toBe(b.id);
  expect(qualifyStudioBackendPath("/workflows/instances/shared-id")).toBe("/workflows/instances/shared-id?backend=b");
  expect(window.location.search).toContain("filter=mine");expect(window.location.hash).toBe("#overview");
});

it("omits ambient cookies on every anonymous backend request",async()=>{
  const request=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>response({}));vi.stubGlobal("fetch",request);
  const selected=new PreparedBackend(a,null,{status:"anonymous",roles:[],permissions:[]});
  try {
    await selected.context.http.getJson("/capabilities");
    await selected.context.http.postJson("/changes",{});
    expect(request).toHaveBeenCalledTimes(2);
    for (const call of request.mock.calls) expect(call[1]?.credentials).toBe("omit");
  } finally {selected.dispose();}
});
