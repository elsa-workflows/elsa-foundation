import {notifyStudioNavigationGuardReleased, createAuthenticatedHttpClient, createEndpointContext, createHttpClient, getBackendCapabilities, invalidateBackendCapabilities, resolveBackendPath, StudioHttpError, type AuthProviderManager, type AuthSession, type StudioApiCapabilityDocument, type StudioBackendProfile, type StudioEndpointContext, type StudioHttpClient, type StudioRequestInit} from "./index";
import type {StudioManagedBackendSession} from "./moduleHost";

export function studioConnectionNonce() {
  return Array.from(crypto.getRandomValues(new Uint8Array(16)),byte=>byte.toString(16).padStart(2,"0")).join("");
}

export class StudioBackendConnection implements StudioManagedBackendSession {
  readonly key: string;
  readonly context: StudioEndpointContext;
  private readonly abort = new AbortController();
  private writes = 0;
  private ended = false;
  private capabilityRefresh = 0;
  capabilities: StudioApiCapabilityDocument | null = null;
  discoveryError?: string;

  constructor(readonly profile: StudioBackendProfile, readonly manager: AuthProviderManager | null, readonly identity: AuthSession) {
    profile = this.profile = Object.freeze({...profile});
    identity = this.identity = Object.freeze({...identity,roles:[...identity.roles],permissions:[...identity.permissions]});
    this.key = JSON.stringify([profile.id,profile.baseUrl,identity.subject ?? "anonymous",identity.tenantId ?? "default",studioConnectionNonce()]);
    const boundAuth = manager ? {
      getAccessToken:async () => {this.assertActive();const token = await manager.getAccessToken();this.assertActive();return token;},
      refresh:async () => {this.assertActive();const session = await manager.refresh();this.assertActive();return session;}
    } : null;
    const raw = manager ? createEndpointContext(profile.baseUrl) : {
      ...createEndpointContext(profile.baseUrl),
      http:createHttpClient(profile.baseUrl,{fetch:(input,init) => fetch(input,{...init,credentials:"omit"})})
    };
    if (manager) raw.http = createAuthenticatedHttpClient(profile.baseUrl,boundAuth!,{requireAuthorization:true,notifySessionLoss:false});
    const run = async <T>(url: string, init: StudioRequestInit | undefined, write: boolean, action: (url:string,init:StudioRequestInit) => Promise<T>, hostAction=false): Promise<T> => {
      this.assertActive();
      if (write) this.writes++;
      try {
        const signal = init?.signal ? AbortSignal.any([this.abort.signal,init.signal]) : this.abort.signal;
        let path:string;
        if (hostAction && /^https?:/i.test(url)) {
          // Prepared workflow HTTP stimuli intentionally target host routes outside the API shell.
          const target = new URL(url);
          if (target.origin !== new URL(profile.baseUrl).origin || target.username || target.password) throw new Error("HTTP actions must stay on the selected backend host.");
          path = target.href;
        } else path = resolveBackendPath(profile.baseUrl,url);
        const result = await action(path,{...init,signal,...(write ? {retryOnUnauthorized:false} : {})});
        this.assertActive();
        return result;
      } finally { if (write && --this.writes === 0) notifyStudioNavigationGuardReleased(); }
    };
    const mutates = (init?: StudioRequestInit) => !["GET","HEAD","OPTIONS"].includes((init?.method ?? "GET").toUpperCase());
    const http: StudioHttpClient = {
      requestJson: (url,init) => run(url,init,mutates(init),(path,options) => raw.http.requestJson(path,options)),
      getJson: (url,init) => run(url,init,false,(path,options) => raw.http.getJson(path,options)),
      postJson: (url,body,init) => run(url,init,true,(path,options) => raw.http.postJson(path,body,options)),
      putJson: (url,body,init) => run(url,init,true,(path,options) => raw.http.putJson(path,body,options)),
      deleteJson: (url,init) => run(url,init,true,(path,options) => raw.http.deleteJson(path,options)),
      postForm: (url,body,init) => run(url,init,true,(path,options) => raw.http.postForm(path,body,options)),
      ...(raw.http.getJsonWithHeaders ? {getJsonWithHeaders:<T,>(url:string,init?:StudioRequestInit) => run(url,init,false,(path,options) => raw.http.getJsonWithHeaders!<T>(path,options))} : {}),
      ...(raw.http.requestResponse ? {requestResponse:(url:string,init?:StudioRequestInit) => run(url,init,mutates(init),(path,options) => raw.http.requestResponse!(path,options),true)} : {})
    };
    this.context = {...raw,http,accessTokenFactory:manager ? async () => { this.assertActive(); const token = await manager.getAccessToken(); this.assertActive(); return token ?? ""; } : undefined};
  }
  get pendingWrites() { return this.writes; }
  private identityMatches() {
    const current = this.manager?.getSession();
    return !current || current.status === this.identity.status && current.subject === this.identity.subject && current.tenantId === this.identity.tenantId;
  }
  get disposed() { return this.ended || !this.identityMatches(); }
  private assertActive() {
    if (this.ended) throw new DOMException("The previous backend connection has ended.","AbortError");
    if (this.manager) {
      const current = this.manager.getSession();
      if (current.status !== this.identity.status || current.subject !== this.identity.subject || current.tenantId !== this.identity.tenantId) {
        this.ended = true;this.abort.abort();invalidateBackendCapabilities(this.context);
        throw new DOMException("The backend identity has changed. Reconnect before continuing.","AbortError");
      }
    }
  }
  async refreshCapabilities() {
    this.assertActive();
    const generation = ++this.capabilityRefresh;
    invalidateBackendCapabilities(this.context);
    try {
      const capabilities = await getBackendCapabilities(this.context);
      this.assertActive();
      if (generation === this.capabilityRefresh) {this.capabilities = capabilities;this.discoveryError = undefined;}
      return capabilities;
    } catch (error) {if (!this.ended && generation === this.capabilityRefresh) {this.capabilities = null;this.discoveryError = studioConnectionError(error);}throw error; }
  }
  dispose() {
    if (this.writes) throw new Error("Wait for backend changes to finish before switching.");
    this.ended = true;
    this.abort.abort();
    invalidateBackendCapabilities(this.context);
  }
}

export function studioConnectionError(error: unknown): string {
  if (error instanceof StudioHttpError && error.status === 401) return "Sign in to this backend to discover its capabilities.";
  if (error instanceof StudioHttpError && error.status === 403) return "You do not have permission to discover this backend's capabilities.";
  return error instanceof Error ? error.message : "The backend could not be reached.";
}

