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
}

export function createAuthProviderManager(options: AuthProviderManagerOptions): AuthProviderManager {
  return new DefaultAuthProviderManager(options);
}

class DefaultAuthProviderManager implements AuthProviderManager {
  private readonly adapters = new Map<string, AuthProviderAdapter>();
  private activeAdapter: AuthProviderAdapter | null = null;
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
    const adapter = await this.resolveAdapter();

    this.session = this.options.isCallback?.()
      ? await adapter.handleCallback()
      : await adapter.initialize();

    return this.session;
  }

  async login(options?: LoginOptions) {
    const adapter = options?.providerId
      ? await this.resolveAdapter(options.providerId)
      : await this.resolveAdapter();

    await adapter.login(options);
  }

  async handleCallback(providerId?: string) {
    const adapter = providerId
      ? await this.resolveAdapter(providerId)
      : await this.resolveAdapter();

    this.session = await adapter.handleCallback();
    return this.session;
  }

  async logout() {
    const adapter = await this.resolveAdapter();
    await adapter.logout();
    this.session = anonymousAuthSession;
  }

  async getAccessToken() {
    const adapter = await this.resolveAdapter();
    return adapter.getAccessToken();
  }

  async refresh() {
    const adapter = await this.resolveAdapter();
    this.session = await adapter.refresh();
    return this.session;
  }

  private async resolveAdapter(providerId?: string) {
    if (providerId) {
      return this.requireAdapter(providerId);
    }

    if (this.activeAdapter) {
      return this.activeAdapter;
    }

    const bootstrap = await this.options.bootstrap();
    const defaultProvider = bootstrap.providers.find(provider => provider.enabled && provider.isDefault)
      ?? bootstrap.providers.find(provider => provider.enabled);

    if (!defaultProvider) {
      throw new AuthConfigurationError("No enabled authentication provider was returned by /_elsa/identity/bootstrap.");
    }

    this.activeAdapter = this.resolveProviderAdapter(defaultProvider);
    return this.activeAdapter;
  }

  private requireAdapter(providerId: string) {
    const adapter = this.adapters.get(providerId);
    if (!adapter) {
      throw new AuthConfigurationError(`No auth provider adapter is registered for '${providerId}'.`);
    }

    this.activeAdapter = adapter;
    return adapter;
  }

  private resolveProviderAdapter(provider: AuthBootstrapProvider) {
    const registered = this.adapters.get(provider.id);
    if (registered) {
      this.activeAdapter = registered;
      return registered;
    }

    if (!this.options.adapterFactory) {
      throw new AuthConfigurationError(`No auth provider adapter is registered for '${provider.id}'.`);
    }

    const adapter = this.options.adapterFactory(provider);
    this.adapters.set(provider.id, adapter);
    this.activeAdapter = adapter;
    return adapter;
  }
}

export class AuthConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AuthConfigurationError";
  }
}
