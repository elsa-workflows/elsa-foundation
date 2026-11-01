import {afterEach,expect,it,vi} from "vitest";
import {initialize} from "../module";
import {createEndpointContext,anonymousAuthSession,type StudioModuleHostContext} from "@elsa-workflows/studio-sdk";
const context=(configuration:Record<string,unknown>):StudioModuleHostContext=>({host:createEndpointContext("http://studio.example/"),identity:anonymousAuthSession,authManager:null,configuration,signal:new AbortController().signal});
const configuration={backendBaseUrl:"http://backend.example/tenant/",extensions:{backends:{configuration:{allowPersonalProfiles:false},profileApiPath:"/studio-shell/_elsa/studio/backends/profiles"}}};
afterEach(()=>{vi.unstubAllGlobals();window.history.replaceState({},"","/");sessionStorage.clear();});
it("requires its feature-owned runtime projection",()=>{expect(()=>initialize(context({}))).toThrow("runtime configuration");});
it("contributes a connection and separate shell placements without requesting backend resources during initialization",()=>{
 const fetch=vi.fn();vi.stubGlobal("fetch",fetch);
 const instance=initialize(context(configuration));
 expect(instance.connection).toBeDefined();expect(instance.shell?.map(item=>item.placement)).toEqual(["connection","notice","recovery"]);expect(fetch).not.toHaveBeenCalled();instance.connection?.dispose?.();
});
it("uses the projected prefixed profile API for the original host identity",async()=>{
 const initial=context({...configuration,extensions:{backends:{configuration:{},profileApiPath:"/studio-shell/_elsa/studio/backends/profiles"}}});
 const host={...initial,identity:{...anonymousAuthSession,status:"authenticated" as const,subject:"alice",tenantId:"tenant-a"}};
 const read=vi.fn(async()=>({revision:"1",value:{profiles:[]}}));host.host.http.getJson=read as typeof host.host.http.getJson;
 vi.stubGlobal("fetch",vi.fn(async()=>Response.json({capabilities:[]})));
 const instance=initialize(host);await instance.connection!.initialize();
 expect(read).toHaveBeenCalledWith("/studio-shell/_elsa/studio/backends/profiles",{signal:expect.any(AbortSignal)});instance.connection?.dispose?.();
});
it("does not discover a backend after teardown during a deferred host profile read",async()=>{
 const initial=context({...configuration,extensions:{backends:{configuration:{},profileApiPath:"/profiles"}}});
 const host={...initial,identity:{...anonymousAuthSession,status:"authenticated" as const,subject:"alice",tenantId:"tenant-a"}};
 let release!:(value:unknown)=>void;host.host.http.getJson=vi.fn(()=>new Promise(resolve=>release=resolve)) as typeof host.host.http.getJson;
 const fetch=vi.fn();vi.stubGlobal("fetch",fetch);
 const instance=initialize(host);const pending=instance.connection!.initialize();instance.connection?.dispose?.();
 release({revision:"1",value:{profiles:[]}});await pending;expect(fetch).not.toHaveBeenCalled();
});
