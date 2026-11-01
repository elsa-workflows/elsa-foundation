import {StudioBackendConnection,studioConnectionError,type AuthProviderManager,type AuthSession,type StudioConnectionController,type StudioConnectionSnapshot,type StudioEndpointContext} from "../sdk";

/** The no-extension default: one configured endpoint, no profiles, persistence or selection storage. */
export class ConfiguredBackendConnection implements StudioConnectionController {
  private snapshot: StudioConnectionSnapshot = {active:null,loading:true,error:null};
  private readonly listeners = new Set<() => void>();
  private ended=false;
  private initializing?: Promise<void>;
  constructor(readonly hostContext:StudioEndpointContext,readonly hostIdentity:AuthSession,
    private readonly manager:AuthProviderManager|null,private readonly baseUrl:string,private readonly id="legacy") {}
  getSnapshot = () => this.snapshot;
  subscribe = (listener:() => void) => {this.listeners.add(listener);return () => {this.listeners.delete(listener);};};
  private update(value:Partial<StudioConnectionSnapshot>) {this.snapshot={...this.snapshot,...value};for(const listener of this.listeners)listener();}
  initialize() {return this.initializing ??= this.initializeCore();}
  private async initializeCore() {
    if(this.ended)return;
    const requested=new URLSearchParams(window.location.search).get("backend");
    if (requested && requested!==this.id) {
      this.update({loading:false,error:"This link refers to a backend that is not available in this Studio. Return Home to use the configured backend."});
      return;
    }
    const active=new StudioBackendConnection({id:this.id,name:"Configured backend",baseUrl:this.baseUrl,environment:"Development",source:"configuration"},this.manager,this.hostIdentity);
    try {await active.refreshCapabilities();}
    catch(error){active.discoveryError=studioConnectionError(error);}
    if(this.ended){active.dispose();return;}
    this.update({active,loading:false,error:active.discoveryError ?? null});
  }
  qualifyPath(path:string) {
    const url=new URL(path,window.location.origin);
    if(url.origin!==window.location.origin) throw new Error("Studio navigation must stay on its host.");
    const target=url.searchParams.get("backend");
    if(target && target!==this.id) {this.update({error:"This link targets a different backend. Return Home to use the configured backend."});return null;}
    url.searchParams.set("backend",this.id);
    return `${url.pathname}${url.search}${url.hash}`;
  }
  dispose() {this.ended=true;if(!this.snapshot.active?.pendingWrites)this.snapshot.active?.dispose();}
}

/** BFCache cannot resurrect retired identity/capability snapshots. */
export function reloadConnectionOnPageRestore(connection:StudioConnectionController,reload=()=>window.location.reload()) {
  const restore=(event:PageTransitionEvent)=>{if(event.persisted && connection.getSnapshot().active)reload();};
  window.addEventListener("pageshow",restore);
  return ()=>window.removeEventListener("pageshow",restore);
}
