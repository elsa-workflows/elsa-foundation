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
      if (!isRedirectChallenge(challenge)) {
        throw new AuthAdapterError(`Provider '${options.id}' does not expose a redirect challenge.`);
      }

      const method = "method" in challenge ? challenge.method.toUpperCase() : "GET";
      if (method !== "GET") {
        throw new AuthAdapterError(`Provider '${options.id}' exposes an unsupported ${method} challenge.`);
      }

      const destination = new URL(getChallengeUrl(challenge), resolveBaseUrl(options));
      const returnUrl = loginOptions?.returnUrl ?? options.location?.href ?? window.location.href;
      destination.searchParams.set("returnUrl", addCallbackProviderToReturnUrl(returnUrl, loginOptions?.providerId ?? options.id, options));
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
      return payload.status ? normalizeSession(payload) : readSession(request, sessionEndpoint, options);
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
  const session = await response.json() as Partial<AuthSession>;
  return normalizeSession(session);
}

function normalizeSession(session: Partial<AuthSession>): AuthSession {
  const status = isAuthSessionStatus(session.status) ? session.status : "anonymous";

  return {
    ...session,
    status,
    roles: normalizeStringArray(session.roles),
    permissions: normalizeStringArray(session.permissions)
  };
}

function normalizeStringArray(value: unknown) {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

function isAuthSessionStatus(value: unknown): value is AuthSession["status"] {
  return value === "unknown" || value === "anonymous" || value === "authenticated";
}

// The first AuthChallenge union member ({ url; method; ... }) has no `type` property, so `.type` can't
// be read off the union directly; this guard both performs the original `!challenge || type === "none"`
// runtime check and narrows the union so getChallengeUrl accepts the result.
function isRedirectChallenge(challenge: AuthChallenge | undefined): challenge is Exclude<AuthChallenge, { type: "none" }> {
  return !!challenge && !("type" in challenge && challenge.type === "none");
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

function addCallbackProviderToReturnUrl(
  returnUrl: string,
  providerId: string,
  options?: Pick<RedirectAuthAdapterOptions, "baseUrl" | "location">
) {
  const result = new URL(returnUrl, resolveReturnUrlBase(options));
  result.searchParams.set("authProviderId", providerId);

  return isRelativeUrl(returnUrl)
    ? `${result.pathname}${result.search}${result.hash}`
    : result.toString();
}

function resolveReturnUrlBase(options?: Pick<RedirectAuthAdapterOptions, "baseUrl" | "location">) {
  return options?.location?.href
    ?? (typeof window !== "undefined" ? window.location.href : undefined)
    ?? options?.location?.origin
    ?? resolveBaseUrl(options);
}

function isRelativeUrl(url: string) {
  try {
    new URL(url);
    return false;
  } catch {
    return true;
  }
}

export class AuthAdapterError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthAdapterError";
  }
}
