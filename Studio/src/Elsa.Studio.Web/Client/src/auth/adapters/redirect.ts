import { anonymousAuthSession, type AuthChallenge, type AuthProviderAdapter, type AuthSession, type LoginOptions } from "../types";

export interface RedirectAuthAdapterOptions {
  id: string;
  kind: string;
  baseUrl?: string;
  challenge?: AuthChallenge;
  sessionEndpoint?: string;
  logoutEndpoint?: string;
  tokenEndpoint?: string;
  refreshEndpoint?: string | null;
  getRefreshToken?: () => string | null | Promise<string | null>;
  fetch?: typeof fetch;
  location?: Pick<Location, "assign" | "href" | "origin">;
}

export function createRedirectAuthAdapter(options: RedirectAuthAdapterOptions): AuthProviderAdapter {
  const request = options.fetch ?? fetch;
  const sessionEndpoint = options.sessionEndpoint ?? "/_elsa/identity/session";
  const logoutEndpoint = options.logoutEndpoint ?? `/_elsa/identity/logout/${encodeURIComponent(options.id)}`;

  return {
    id: options.id,
    kind: options.kind,
    initialize: () => readSession(request, sessionEndpoint, options),
    login: loginOptions => {
      const challenge = options.challenge;
      if (!challenge || challenge.type === "none") {
        throw new AuthAdapterError(`Provider '${options.id}' does not expose a redirect challenge.`);
      }

      const method = "method" in challenge ? challenge.method.toUpperCase() : "GET";
      if (method !== "GET") {
        throw new AuthAdapterError(`Provider '${options.id}' exposes an unsupported ${method} challenge.`);
      }

      const destination = new URL(getChallengeUrl(challenge), resolveBaseUrl(options));
      const returnUrl = loginOptions?.returnUrl ?? options.location?.href ?? window.location.href;
      destination.searchParams.set("returnUrl", returnUrl);
      (options.location ?? window.location).assign(destination.toString());
      return Promise.resolve();
    },
    handleCallback: () => readSession(request, sessionEndpoint, options),
    logout: async () => {
      const response = await request(resolveAuthUrl(logoutEndpoint, options), { method: "POST", credentials: "include" });
      if (!response.ok) {
        throw new AuthAdapterError(`Sign-out failed with ${response.status}.`);
      }
    },
    getAccessToken: async () => {
      if (!options.tokenEndpoint) {
        return null;
      }

      const response = await request(resolveAuthUrl(options.tokenEndpoint, options), { credentials: "include", cache: "no-store" });
      if (response.status === 401) {
        return null;
      }
      if (!response.ok) {
        throw new AuthAdapterError(`Access-token request failed with ${response.status}.`);
      }

      const payload = await response.json() as { accessToken?: unknown };
      return typeof payload.accessToken === "string" ? payload.accessToken : null;
    },
    refresh: async () => {
      const refreshToken = await options.getRefreshToken?.();
      const refreshEndpoint = options.refreshEndpoint;
      if (!refreshEndpoint || !refreshToken) {
        return readSession(request, sessionEndpoint, options);
      }

      const response = await request(resolveAuthUrl(refreshEndpoint, options), {
        method: "POST",
        credentials: "include",
        headers: {
          Accept: "application/json",
          "Content-Type": "application/json"
        },
        body: JSON.stringify({ refreshToken })
      });
      if (response.status === 401) {
        return anonymousAuthSession;
      }
      if (!response.ok) {
        throw new AuthAdapterError(`Session refresh failed with ${response.status}.`);
      }

      const payload = await response.json() as Partial<AuthSession>;
      return payload.status ? normalizeSession(payload as AuthSession) : readSession(request, sessionEndpoint, options);
    }
  };
}

async function readSession(request: typeof fetch, sessionEndpoint: string, options?: Pick<RedirectAuthAdapterOptions, "baseUrl" | "location">): Promise<AuthSession> {
  const response = await request(resolveAuthUrl(sessionEndpoint, options), { credentials: "include", cache: "no-store" });
  if (response.status === 401) {
    return anonymousAuthSession;
  }
  if (!response.ok) {
    throw new AuthAdapterError(`Session request failed with ${response.status}.`);
  }

  return parseSession(response);
}

async function parseSession(response: Response) {
  const session = await response.json() as AuthSession;
  return normalizeSession(session);
}

function normalizeSession(session: AuthSession): AuthSession {
  return {
    ...session,
    roles: session.roles ?? [],
    permissions: session.permissions ?? []
  };
}

function getChallengeUrl(challenge: Exclude<AuthChallenge, { type: "none" }>) {
  return "loginPath" in challenge ? challenge.loginPath : challenge.url;
}

function resolveAuthUrl(url: string, options?: Pick<RedirectAuthAdapterOptions, "baseUrl" | "location">) {
  return new URL(url, resolveBaseUrl(options)).toString();
}

function resolveBaseUrl(options?: Pick<RedirectAuthAdapterOptions, "baseUrl" | "location">) {
  return options?.baseUrl ?? options?.location?.origin ?? window.location.origin;
}

export class AuthAdapterError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthAdapterError";
  }
}
