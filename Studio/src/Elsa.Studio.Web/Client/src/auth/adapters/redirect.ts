import { anonymousAuthSession, type AuthChallenge, type AuthProviderAdapter, type AuthSession, type LoginOptions } from "../types";

export interface RedirectAuthAdapterOptions {
  id: string;
  kind: string;
  challenge?: AuthChallenge;
  sessionEndpoint?: string;
  logoutEndpoint?: string;
  tokenEndpoint?: string;
  refreshEndpoint?: string;
  fetch?: typeof fetch;
  location?: Pick<Location, "assign" | "href" | "origin">;
}

export function createRedirectAuthAdapter(options: RedirectAuthAdapterOptions): AuthProviderAdapter {
  const request = options.fetch ?? fetch;
  const sessionEndpoint = options.sessionEndpoint ?? "/_elsa/identity/session";
  const logoutEndpoint = options.logoutEndpoint ?? "/_elsa/identity/logout";
  const refreshEndpoint = options.refreshEndpoint ?? "/_elsa/identity/refresh";

  return {
    id: options.id,
    kind: options.kind,
    initialize: () => readSession(request, sessionEndpoint),
    login: loginOptions => {
      const challenge = options.challenge;
      if (!challenge || challenge.type !== "redirect") {
        throw new AuthAdapterError(`Provider '${options.id}' does not expose a redirect challenge.`);
      }

      const destination = new URL(challenge.loginPath, options.location?.origin ?? window.location.origin);
      const returnUrl = loginOptions?.returnUrl ?? options.location?.href ?? window.location.href;
      destination.searchParams.set("returnUrl", returnUrl);
      (options.location ?? window.location).assign(destination.toString());
      return Promise.resolve();
    },
    handleCallback: () => readSession(request, sessionEndpoint),
    logout: async () => {
      const response = await request(logoutEndpoint, { method: "POST", credentials: "include" });
      if (!response.ok) {
        throw new AuthAdapterError(`Sign-out failed with ${response.status}.`);
      }
    },
    getAccessToken: async () => {
      if (!options.tokenEndpoint) {
        return null;
      }

      const response = await request(options.tokenEndpoint, { credentials: "include", cache: "no-store" });
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
      const response = await request(refreshEndpoint, { method: "POST", credentials: "include" });
      if (response.status === 401) {
        return anonymousAuthSession;
      }
      if (!response.ok) {
        throw new AuthAdapterError(`Session refresh failed with ${response.status}.`);
      }

      return parseSession(response);
    }
  };
}

async function readSession(request: typeof fetch, sessionEndpoint: string): Promise<AuthSession> {
  const response = await request(sessionEndpoint, { credentials: "include", cache: "no-store" });
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
  return {
    ...session,
    roles: session.roles ?? [],
    permissions: session.permissions ?? []
  };
}

export class AuthAdapterError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthAdapterError";
  }
}
