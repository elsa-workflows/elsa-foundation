import {
  anonymousAuthSession,
  unknownAuthSession,
  type AuthBootstrap,
  type AuthBootstrapProvider,
  type AuthCapabilities,
  type AuthProviderAdapter,
  type AuthProviderManager,
  type AuthSession,
  type LoginOptions
} from "./types";

export interface AuthProviderManagerOptions {
  bootstrap: () => Promise<AuthBootstrap>;
  capabilities: () => Promise<AuthCapabilities>;
  adapters?: AuthProviderAdapter[];
  adapterFactory?: (provider: AuthBootstrapProvider) => AuthProviderAdapter;
  isCallback?: () => boolean;
  getCallbackProviderId?: () => string | null | undefined;
}

export function createAuthProviderManager(options: AuthProviderManagerOptions): AuthProviderManager {
  return new DefaultAuthProviderManager(options);
}

class DefaultAuthProviderManager implements AuthProviderManager {
  private readonly adapters = new Map<string, AuthProviderAdapter>();
  private activeAdapter: AuthProviderAdapter | null = null;
  private pendingLoginProviderId: string | null = null;
  private session: AuthSession = unknownAuthSession;

  constructor(private readonly options: AuthProviderManagerOptions) {
    for (const adapter of options.adapters ?? []) {
      if (this.adapters.has(adapter.id)) {
        throw new AuthConfigurationError(`Duplicate auth provider adapter '${adapter.id}'.`);
      }

      this.adapters.set(adapter.id, adapter);
    }
  }

  getSession() {
    return this.session;
  }

  getCapabilities() {
    return this.options.capabilities();
  }

  async initialize() {
    if (this.options.isCallback?.()) {
      const callbackProviderId = this.getCallbackProviderId();
      const adapter = callbackProviderId
        ? await this.getProviderAdapter(callbackProviderId)
        : await this.resolveActiveAdapter();

      await this.applySession(await adapter.handleCallback(), adapter);
      this.pendingLoginProviderId = null;
      return this.session;
    }

    const adapter = await this.resolveActiveAdapter();
    await this.applySession(await adapter.initialize(), adapter);
    this.pendingLoginProviderId = null;

    return this.session;
  }

  async login(options?: LoginOptions) {
    const adapter = options?.providerId
      ? await this.getProviderAdapter(options.providerId)
      : await this.resolveActiveAdapter();

    this.pendingLoginProviderId = adapter.id;
    try {
      const loginSession = await adapter.login({ ...options, providerId: adapter.id });
      if (loginSession) {
        await this.applySession(loginSession, adapter);
        this.pendingLoginProviderId = null;
      } else if (this.session.status !== "authenticated") {
        this.activeAdapter = adapter;
      }
    } catch (error) {
      this.pendingLoginProviderId = null;
      throw error;
    }
  }

  async handleCallback(providerId?: string) {
    const adapter = providerId
      ? await this.getProviderAdapter(providerId)
      : await this.resolveActiveAdapter();

    await this.applySession(await adapter.handleCallback(), adapter);
    this.pendingLoginProviderId = null;
    return this.session;
  }

  async logout() {
    const adapter = await this.resolveActiveAdapter();
    await adapter.logout();
    this.session = anonymousAuthSession;
  }

  async getAccessToken() {
    const adapter = await this.resolveActiveAdapter();
    return adapter.getAccessToken();
  }

  async refresh() {
    const adapter = await this.resolveActiveAdapter();
    await this.applySession(await adapter.refresh(), adapter);
    return this.session;
  }

  private async resolveActiveAdapter() {
    if (this.activeAdapter) {
      return this.activeAdapter;
    }

    const bootstrap = await this.options.bootstrap();
    const defaultProvider = bootstrap.providers.find(provider => provider.enabled && provider.isDefault)
      ?? bootstrap.providers.find(provider => provider.enabled);

    if (!defaultProvider) {
      throw new AuthConfigurationError("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    }

    const adapter = this.resolveProviderAdapter(defaultProvider);
    this.activeAdapter = adapter;
    return adapter;
  }

  private async getProviderAdapter(providerId: string) {
    const registered = this.adapters.get(providerId);
    if (registered) {
      return registered;
    }

    const bootstrap = await this.options.bootstrap();
    const provider = bootstrap.providers.find(candidate => candidate.enabled && candidate.id === providerId);
    if (!provider) {
      throw new AuthConfigurationError(`No auth provider adapter is registered for '${providerId}'.`);
    }

    return this.resolveProviderAdapter(provider);
  }

  private resolveProviderAdapter(provider: AuthBootstrapProvider) {
    const registered = this.adapters.get(provider.id);
    if (registered) {
      return registered;
    }

    if (!this.options.adapterFactory) {
      throw new AuthConfigurationError(`No auth provider adapter is registered for '${provider.id}'.`);
    }

    const adapter = this.options.adapterFactory(provider);
    this.adapters.set(provider.id, adapter);
    return adapter;
  }

  private getCallbackProviderId() {
    const configuredProviderId = this.options.getCallbackProviderId?.();
    if (configuredProviderId) {
      return configuredProviderId;
    }

    if (this.pendingLoginProviderId) {
      return this.pendingLoginProviderId;
    }

    if (typeof window === "undefined") {
      return null;
    }

    return new URLSearchParams(window.location.search).get("authProviderId");
  }

  private async applySession(session: AuthSession, fallbackAdapter: AuthProviderAdapter) {
    this.session = session;
    this.activeAdapter = session.provider?.id
      ? await this.getProviderAdapter(session.provider.id)
      : fallbackAdapter;
  }
}

export class AuthConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthConfigurationError";
  }
}
