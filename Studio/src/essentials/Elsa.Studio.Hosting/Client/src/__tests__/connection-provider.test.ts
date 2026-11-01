import {afterEach,describe,expect,it,vi} from "vitest";
import {initializeStudioModules,registerStudioModuleInstances} from "../app/loader";
import {ConfiguredBackendConnection} from "../app/connection";
import {createStudioRegistry} from "../app/registry";
import {StudioBackendConnection,anonymousAuthSession,createEndpointContext,type StudioModuleHostContext,type StudioModuleManifest,type StudioConnectionController} from "../sdk";
const host:StudioModuleHostContext={host:createEndpointContext("http://localhost:3000"),identity:anonymousAuthSession,authManager:null,configuration:{},signal:new AbortController().signal};
const manifest=(id:string):StudioModuleManifest=>({id,displayName:id,entry:`/${id}.js`,version:"1",styles:[],requiredHostVersion:"*",requiredSdkVersion:"*",capabilities:["host-initialization"]});
const options={hostVersion:"1",sdkVersion:"1"};
afterEach(()=>{vi.unstubAllGlobals();window.history.replaceState({},"","/");});
describe("document-owned connection providers",()=>{
 it("keeps core's fixed backend gated by real discovery with no profile request or selection storage",async()=>{
  sessionStorage.setItem("elsa.studio.backend.selection.v1","foreign");
  const fetch=vi.fn(async()=>Response.json({capabilities:[]}));vi.stubGlobal("fetch",fetch);
  const connection=new ConfiguredBackendConnection(host.host,host.identity,null,"http://backend.example/shell/");
  await connection.initialize();
  expect(connection.getSnapshot().active?.profile.id).toBe("legacy");
  expect(connection.getSnapshot().active?.capabilities).toEqual({capabilities:[]});
  expect(fetch).toHaveBeenCalledTimes(1);expect(String(fetch.mock.calls[0][0])).toContain("/shell/capabilities");
 });
 it("qualifies an initial deep link before publishing the default session",async()=>{
  window.history.replaceState({},"","/workflows/shared?definition=resource#details");
  vi.stubGlobal("fetch",vi.fn(async()=>Response.json({capabilities:[]})));
  const connection=new ConfiguredBackendConnection(host.host,host.identity,null,"http://backend.example/shell/");
  const ready=vi.fn(()=>{if(connection.getSnapshot().active)expect(window.location.search).toBe("?definition=resource&backend=legacy");});
  connection.subscribe(ready);await connection.initialize();
  expect(window.location.pathname).toBe("/workflows/shared");expect(window.location.hash).toBe("#details");expect(ready).toHaveBeenCalled();
 });
 it("refuses a foreign resource link before calling the configured backend",async()=>{
  window.history.replaceState({},"","/workflows/foreign?backend=retired");
  const fetch=vi.fn();vi.stubGlobal("fetch",fetch);
  const connection=new ConfiguredBackendConnection(host.host,host.identity,null,"http://backend.example/shell/");
  await connection.initialize();
  expect(connection.getSnapshot()).toMatchObject({active:null,loading:false,error:expect.stringContaining("not available")});
  expect(fetch).not.toHaveBeenCalled();
 });
 it("initializes a differently named extension once before registration and shares its connection with fresh registries",async()=>{
  const connection=new ConfiguredBackendConnection(host.host,host.identity,null,"http://backend.example/");
  const register=vi.fn(api=>{expect(api.connection).toBe(connection);});
  const initialize=vi.fn(()=>({connection,register,shell:[{id:"acme",placement:"connection" as const,component:()=>null}]}));
  const loaded=await initializeStudioModules([manifest("acme")],host,{...options,importModule:async()=>({initialize})});
  expect(initialize).toHaveBeenCalledTimes(1);expect(register).not.toHaveBeenCalled();
  for(let i=0;i<2;i++){
   const api=createStudioRegistry({...host.host,...options},{connection});await registerStudioModuleInstances(loaded.instances,api);
   expect(api.shell!.list()).toHaveLength(1);
  }
  expect(register).toHaveBeenCalledTimes(2);expect(initialize).toHaveBeenCalledTimes(1);
 });
 it.each(["host","sdk"])("reports the required %s version before importing an incompatible module",async(kind)=>{
  const incompatible={...manifest("acme"),capabilities:[],[kind==="host"?"requiredHostVersion":"requiredSdkVersion"]:"999.0.0"};
  const importModule=vi.fn();
  const loaded=await initializeStudioModules([incompatible],host,{...options,importModule});
  expect(loaded.instances).toEqual([]);expect(importModule).not.toHaveBeenCalled();
  expect(loaded.diagnostics).toEqual([{moduleId:"acme",status:"incompatible",reason:`Requires ${kind==="host"?"host":"SDK"} 999.0.0.`}]);
 });
 it("fails duplicate connection providers explicitly and disposes their instances",async()=>{
  const dispose=vi.fn();const connection={getSnapshot:()=>({active:null})} as StudioConnectionController;
  await expect(initializeStudioModules([manifest("acme"),manifest("other")],host,{...options,importModule:async()=>({initialize:()=>({connection,dispose,register(){}})})})).rejects.toThrow("Only one");
  expect(dispose).toHaveBeenCalledTimes(2);
 });
 it("does not silently fall back when a host extension cannot initialize or import",async()=>{
  await expect(initializeStudioModules([manifest("acme")],host,{...options,importModule:async()=>{throw Error("unavailable");}})).rejects.toThrow("unavailable");
  await expect(initializeStudioModules([{...manifest("acme"),capabilities:[]}],host,{...options,importModule:async()=>({initialize(){throw Error("broken provider");}})})).rejects.toThrow("broken provider");
 });
 it("retires in-flight writes at document teardown and fences a transport's late response",async()=>{
  let finish!:(response:Response)=>void;
  const request=vi.fn(()=>new Promise<Response>(resolve=>finish=resolve));vi.stubGlobal("fetch",request);
  const active=new StudioBackendConnection({id:"fixed",name:"Fixed",baseUrl:"http://backend.example/shell/",environment:"Development",source:"configuration"},null,anonymousAuthSession);
  const write=active.context.http.postJson("workflows",{name:"Pending write"});
  await vi.waitFor(()=>expect(request).toHaveBeenCalledOnce());
  expect(active.pendingWrites).toBe(1);expect(()=>active.dispose()).toThrow("Wait for backend changes");
  active.retire();expect(active.disposed).toBe(true);
  finish(Response.json({id:"late-result"}));
  await expect(write).rejects.toMatchObject({name:"AbortError"});expect(active.pendingWrites).toBe(0);
  await expect(active.context.http.getJson("workflows")).rejects.toMatchObject({name:"AbortError"});expect(request).toHaveBeenCalledOnce();
 });

});
