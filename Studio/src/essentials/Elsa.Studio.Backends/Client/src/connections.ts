import {
  anonymousAuthSession, createBackendAuthProviderManager, requestStudioNavigation, StudioHttpError,
  studioBackendCapabilitiesChangedEvent, StudioBackendConnection as PreparedBackend,
  studioConnectionError as connectionError,
  type AuthProviderManager, type AuthSession, type StudioBackendProfile, type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
export { StudioBackendConnection as PreparedBackend, studioConnectionError as connectionError } from "@elsa-workflows/studio-sdk";
import type { BackendRuntimeConfig } from "./runtime";


const profileLimit = 32;
const profileBytes = 32 * 1024;
const selectionKey = "elsa.studio.backend.selection.v1";
const pendingKey = "elsa.studio.backend.sign-in.v1";

function readSessionValue(key: string): string | null {
  try { return sessionStorage.getItem(key); }
  catch { return null; }
}

/** Opaque cryptographic generation IDs also work on HTTP development hosts. */
export function backendConnectionNonce() {
  return Array.from(crypto.getRandomValues(new Uint8Array(16)),byte=>byte.toString(16).padStart(2,"0")).join("");
}

export function canonicalBackendUrl(value: string, origin = window.location.origin) {
  const url = new URL(value || origin, origin);
  if (!/^https?:$/.test(url.protocol) || url.username || url.password || url.search || url.hash || value.includes("\\") || value.length > 2048)
    throw new Error("Use an HTTP or HTTPS API URL without credentials, a query or a fragment.");
  url.pathname = `${url.pathname.replace(/\/+$/, "")}/`;
  return url.toString();
}

export function validatePersonalProfiles(value: unknown): StudioBackendProfile[] {
  if (!Array.isArray(value) || value.length > profileLimit) throw new Error("Save at most 32 personal backends.");
  if (new TextEncoder().encode(JSON.stringify(value)).length > profileBytes) throw new Error("Backend profiles exceed the preference size limit.");
  const ids = new Set<string>();
  return value.map((entry: unknown) => {
    if (!entry || typeof entry !== "object") throw new Error("Invalid backend profile.");
    const item = entry as Record<string, unknown>;
    if (Object.keys(item).some(key => !["id","name","baseUrl","environment","authenticationProfile"].includes(key))
      || typeof item.id !== "string" || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/.test(item.id) || item.id === "legacy" || ids.has(item.id)
      || typeof item.name !== "string" || !item.name.trim() || item.name.length > 80
      || typeof item.baseUrl !== "string" || !/^https?:\/\//.test(item.baseUrl)
      || !["Development","Test","Production"].includes(item.environment as string)
      || (item.authenticationProfile !== undefined && (typeof item.authenticationProfile !== "string" || item.authenticationProfile.length > 64)))
      throw new Error("Invalid backend profile metadata.");
    ids.add(item.id);
    return { id:item.id, name:item.name.trim(), baseUrl:canonicalBackendUrl(item.baseUrl), environment:item.environment as StudioBackendProfile["environment"],
      ...(item.authenticationProfile ? {authenticationProfile:item.authenticationProfile as string} : {}), source:"personal" };
  });
}

function metadata(profile: StudioBackendProfile) {
  const {id,name,baseUrl,environment,authenticationProfile} = profile;
  return {id,name,baseUrl,environment,...(authenticationProfile ? {authenticationProfile} : {})};
}

export interface BackendConnectionsSnapshot {
  profiles: StudioBackendProfile[];
  active: PreparedBackend | null;
  loading: boolean;
  busy: boolean;
  error: string | null;
  profileError: string | null;
  signInProfile: string | null;
}

/** One controller per Studio host identity and browser document. Selected backend state never owns it. */
export class BackendConnections {
  private snapshot: BackendConnectionsSnapshot;
  private readonly listeners = new Set<() => void>();
  private readonly configured: StudioBackendProfile[];
  private personal: StudioBackendProfile[] = [];
  private revision?: string;
  private operation = 0;
  private ended = false;
  private initializing?: Promise<void>;
  private signInCandidate: {profile:StudioBackendProfile;manager:AuthProviderManager} | null = null;
  private readonly localKey: string;
  readonly defaultId: string;
  readonly allowPersonal: boolean;

  constructor(readonly config: BackendRuntimeConfig, readonly hostContext: StudioEndpointContext, readonly hostManager: AuthProviderManager | null,
    readonly hostIdentity: AuthSession, private readonly commitNavigation: (url:string) => void = url => window.location.assign(url)) {
    const declared = config.backends?.profiles ?? [];
    this.configured = declared.length ? declared.map(item => ({...item,baseUrl:canonicalBackendUrl(item.baseUrl),source:"configuration" as const})) : [
      {id:"legacy",name:"Default backend",baseUrl:canonicalBackendUrl(config.backendBaseUrl ?? ""),environment:"Development",source:"configuration"}
    ];
    this.defaultId = config.backends?.defaultId ?? this.configured[0].id;
    this.allowPersonal = config.backends?.allowPersonalProfiles !== false;
    this.localKey = `elsa.studio.backend-profiles.v1:${JSON.stringify([window.location.origin,config.hostId ?? "default",hostIdentity.subject ?? "anonymous",hostIdentity.tenantId ?? "default"])}`;
    this.snapshot = {profiles:this.configured,active:null,loading:true,busy:false,error:null,profileError:null,signInProfile:null};
  }
  dispose() {this.ended=true;this.operation++;this.signInCandidate=null;if(!this.snapshot.active?.pendingWrites)this.snapshot.active?.dispose();this.listeners.clear();}
  getSnapshot = () => this.snapshot;
  subscribe = (listener:() => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  private update(value: Partial<BackendConnectionsSnapshot>) { this.snapshot = {...this.snapshot,...value}; for (const listener of this.listeners) listener(); }
  private async loadPersonal() {
    if (!this.allowPersonal) return;
    if (this.hostIdentity.status !== "authenticated") {
      const stored = localStorage.getItem(this.localKey);
      if (stored) {
        const value = JSON.parse(stored) as {revision?:string;profiles:unknown};
        this.personal = validatePersonalProfiles(value.profiles);
        this.revision = value.revision;
      }
    } else {
      try {
        const document = await this.hostContext.http.getJson<{revision:string;value:{profiles:unknown}}>(this.config.profileApiPath);
        this.personal = validatePersonalProfiles(document.value.profiles);
        this.revision = document.revision;
      } catch (error) { if (!(error instanceof StudioHttpError && error.status === 404)) throw error; }
    }
    const configured = new Set(this.configured.map(item => item.id));
    this.personal = this.personal.filter(item => !configured.has(item.id));
  }
  initialize() { return this.initializing ??= this.initializeCore(); }
  private async initializeCore() {
    try { await this.loadPersonal(); }
    catch (error) { this.update({profileError:`Personal profiles could not be loaded. ${connectionError(error)}`}); }
    if(this.ended)return;
    this.update({profiles:[...this.configured,...this.personal]});
    const query = new URLSearchParams(window.location.search);
    // A resource URL without a backend reference means the configured default, never another tab's last choice.
    const id = query.get("backend") ?? (window.location.pathname === "/" ? readSessionValue(selectionKey) : null) ?? this.defaultId;
    const profile = this.snapshot.profiles.find(item => item.id === id);
    if (!profile) { this.update({loading:false,error:"The backend referenced by this link is unavailable. Choose a backend in Settings."}); return; }
    try {
      const active = await this.prepare(profile);
      if(this.ended){active.dispose();return;}
      // Resource hrefs must carry even a tab-restored selection before modules render.
      const selectedUrl = new URL(window.location.href);
      selectedUrl.searchParams.set("backend",profile.id);
      window.history.replaceState(window.history.state,"",`${selectedUrl.pathname}${selectedUrl.search}${selectedUrl.hash}`);
      this.update({active,loading:false});
    }
    catch (error) {
      if(this.ended)return;
      const active = new PreparedBackend(profile,null,anonymousAuthSession);
      active.discoveryError = connectionError(error);
      this.update({active,loading:false,error:active.discoveryError});
    }
    const pending = readSessionValue(pendingKey);
    if (pending && query.has("backendConnect")) {
      try {
        sessionStorage.removeItem(pendingKey);
        const saved = JSON.parse(pending) as {id:string;baseUrl:string;time:number};
        const target = this.snapshot.profiles.find(item => item.id === saved.id && item.baseUrl === saved.baseUrl);
        if (target && query.get("backendConnect") === target.id && Date.now()-saved.time < 10*60_000) await this.connect(target.id,true);
      } catch { this.update({error:"The saved sign-in could not be resumed. Choose the backend again to connect."}); }
    }
  }
  private async prepare(profile:StudioBackendProfile,operation=this.operation): Promise<PreparedBackend> {
    const auth = profile.authenticationProfile ? this.config.backends?.authenticationProfiles?.[profile.authenticationProfile] : undefined;
    if (profile.authenticationProfile && !auth) throw new Error("This profile references an unknown authentication profile.");
    const original = canonicalBackendUrl(this.config.backendBaseUrl ?? "");
    let manager: AuthProviderManager | null = profile.baseUrl === original ? this.hostManager : null;
    let identity = manager ? manager.getSession() : anonymousAuthSession;
    if (!manager && auth?.enabled !== true) {
      const anonymous = new PreparedBackend(profile,null,anonymousAuthSession);
      try { await anonymous.refreshCapabilities(); return anonymous; }
      catch (error) { anonymous.dispose(); if (!(error instanceof StudioHttpError && error.status === 401) || auth?.enabled === false) throw error; }
    }
    if (!manager) {
      const baseUrl = profile.baseUrl;
      manager = createBackendAuthProviderManager({baseUrl,tokenEndpoint:auth?.tokenEndpoint,refreshEndpoint:auth?.refreshEndpoint,shellRelative:true});
      identity = await manager.initialize();
    }
    if (manager && identity.status !== "authenticated") {
      if (operation === this.operation) {this.signInCandidate = {profile,manager};this.update({signInProfile:profile.id});}
      throw new Error("Sign in to this backend before connecting.");
    }
    const prepared = new PreparedBackend(profile,manager,identity);
    try { await prepared.refreshCapabilities(); return prepared; }
    catch (error) { prepared.dispose(); throw error; }
  }
  private guard() {
    if (this.snapshot.active?.pendingWrites) throw new Error("Wait for backend changes to finish before switching.");
    if (!requestStudioNavigation(`${window.location.pathname}${window.location.search}`,"/settings/backends")) throw new Error("The backend was not changed. Finish or discard the current draft first.");
  }
  async connect(id:string,signInReturn=false) {
    if (this.snapshot.busy) return;
    const operation = ++this.operation;
    const profile = this.snapshot.profiles.find(item => item.id === id);
    if (!profile) { this.update({error:"This backend profile is no longer available."}); return; }
    this.update({error:null,signInProfile:null});
    let candidate: PreparedBackend | null = null;
    try {
      this.guard();
      if (profile.environment === "Production" && !signInReturn && !window.confirm(`Connect to production backend “${profile.name}”?`)) return;
      this.update({busy:true});
      candidate = await this.prepare(profile);
      if (operation !== this.operation) { candidate.dispose(); return; }
      this.guard();
      // A beforeunload prompt may cancel location.assign. End the old lifetime only once the document leaves.
      const previous = this.snapshot.active;
      window.addEventListener("pagehide",() => {if (!previous?.pendingWrites) previous?.dispose();},{once:true});
      try {sessionStorage.setItem(selectionKey,profile.id);} catch { /* Selection remains explicit in the committed URL. */ }
      this.commitNavigation(`/?backend=${encodeURIComponent(profile.id)}`);
    } catch (error) { if (operation === this.operation) this.update({error:connectionError(error)}); }
    finally { if (candidate && !candidate.disposed) candidate.dispose(); if (operation === this.operation) this.update({busy:false}); }
  }
  cancelConnection() { this.operation++; this.signInCandidate = null; this.update({busy:false,signInProfile:null}); }
  async signIn() {
    const candidate = this.signInCandidate;
    if (!candidate) return;
    this.guard();
    sessionStorage.setItem(pendingKey,JSON.stringify({id:candidate.profile.id,baseUrl:candidate.profile.baseUrl,time:Date.now()}));
    const url = new URL("/settings/backends",window.location.origin);
    if (this.snapshot.active) url.searchParams.set("backend",this.snapshot.active.profile.id);
    url.searchParams.set("backendConnect",candidate.profile.id);
    await candidate.manager.login({returnUrl:url.toString()});
  }
  async inspect(id:string) {
    const profile = this.snapshot.profiles.find(item => item.id === id);
    if (!profile) throw new Error("This backend profile is unavailable.");
    if (profile.id === this.snapshot.active?.profile.id && !this.snapshot.active.disposed) {
      const active = this.snapshot.active;
      try {return await active.refreshCapabilities();}
      finally {
        if (this.snapshot.active === active) this.update({active});
        window.dispatchEvent(new CustomEvent(studioBackendCapabilitiesChangedEvent,{detail:{key:active.key,refreshed:true}}));
      }
    }
    const candidate = await this.prepare(profile);
    const capabilities = candidate.capabilities!;
    candidate.dispose();
    return capabilities;
  }
  async saveProfiles(profiles:StudioBackendProfile[]) {
    if (!this.allowPersonal) throw new Error("Personal backend profiles are disabled by your Studio host.");
    const personal = validatePersonalProfiles(profiles.map(metadata));
    if (personal.some(item => this.configured.some(configured => configured.id === item.id))) throw new Error("A configuration-managed backend cannot be replaced.");
    const value = {profiles:personal.map(metadata)};
    if (this.hostIdentity.status !== "authenticated") {
      const stored = localStorage.getItem(this.localKey);
      const current = stored ? (JSON.parse(stored) as {revision:string}).revision : undefined;
      if (current !== this.revision) throw new Error("Backend profiles changed in another tab. Reload before saving.");
      const revision = backendConnectionNonce();
      localStorage.setItem(this.localKey,JSON.stringify({...value,revision}));
      this.revision = revision;
    } else {
      const document = await this.hostContext.http.putJson<{revision:string}>(this.config.profileApiPath,{schemaVersion:1,value},{headers:this.revision ? {"If-Match":`"${this.revision}"`} : {"If-None-Match":"*"}});
      this.revision = document.revision;
    }
    this.personal = personal;
    this.update({profiles:[...this.configured,...personal],profileError:null});
  }
  async save(profile:StudioBackendProfile) {
    const existing = this.snapshot.profiles.find(item => item.id === profile.id);
    if (existing?.source === "configuration") throw new Error("This backend is managed in Studio configuration.");
    if (existing && existing.id === this.snapshot.active?.profile.id && canonicalBackendUrl(profile.baseUrl) !== existing.baseUrl)
      throw new Error("Connect to another backend before changing this active profile's URL.");
    await this.saveProfiles([...this.personal.filter(item => item.id !== profile.id),profile]);
  }
  async remove(id:string) {
    if (this.snapshot.active?.profile.id === id) throw new Error("Connect to another backend before removing this profile.");
    if (this.configured.some(item => item.id === id)) throw new Error("This backend is managed in Studio configuration.");
    await this.saveProfiles(this.personal.filter(item => item.id !== id));
  }
  qualifyPath(path:string) {
    const url = new URL(path,window.location.origin);
    if (url.origin !== window.location.origin) throw new Error("Studio navigation must stay on the Studio host.");
    const target=url.searchParams.get("backend");
    const active=this.snapshot.active?.profile.id;
    if (target && active && target !== active) {
      this.update({error:"This link targets another backend. Connect to that backend before opening the resource."});
      return null;
    }
    if (active) url.searchParams.set("backend",active);
    return `${url.pathname}${url.search}${url.hash}`;
  }
}


/** A cached document must rediscover identity and capabilities before showing retained backend data. */
export function reloadBackendOnPageRestore(connections:BackendConnections,reload=()=>window.location.reload()) {
  const restore=(event:PageTransitionEvent)=>{if(event.persisted && connections.getSnapshot().active) reload();};
  window.addEventListener("pageshow",restore);
  return ()=>window.removeEventListener("pageshow",restore);
}
