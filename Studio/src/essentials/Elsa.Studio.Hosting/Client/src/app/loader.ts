import type {
  ElsaStudioModule,
  ElsaStudioModuleApi,
  StudioModuleDiagnostic,
  StudioModuleManifest
} from "../sdk";
import type {StudioModuleHostContext, StudioModuleInstance} from "../sdk";

export interface StudioLoadedModule {manifest: StudioModuleManifest; instance: StudioModuleInstance}

export function disposeStudioModuleInstance(instance:StudioModuleInstance) {
  if(!instance.connection?.getSnapshot().active?.pendingWrites)instance.connection?.dispose?.();
  instance.dispose?.();
}

/** Imports and initializes document-owned extensions before any backend-bound module registration. */
export async function initializeStudioModules(manifests: StudioModuleManifest[], host: StudioModuleHostContext, options: LoadModulesOptions) {
  const instances: StudioLoadedModule[] = [];
  const diagnostics: StudioModuleDiagnostic[] = [];
  try {
    for (const manifest of manifests) {
      if(host.signal.aborted)throw new DOMException("Module initialization was cancelled.","AbortError");
      if (!isVersionRangeCompatible(manifest.requiredHostVersion,options.hostVersion)
        || !isVersionRangeCompatible(manifest.requiredSdkVersion,options.sdkVersion)) {
        if (manifest.capabilities.includes("host-initialization")) throw new Error(`Host extension ${manifest.displayName} is incompatible.`);
        diagnostics.push({moduleId:manifest.id,status:"incompatible",reason:"The module requires another host or SDK version."});
        continue;
      }
      let instance: StudioModuleInstance | undefined;
      let initializing = false;
      try {
        for (const style of manifest.styles) await (options.loadStyle ?? defaultLoadStyle)(manifest.stylesHaveContentHashes ? style : addModuleVersion(style,manifest.version));
        const module = await (options.importModule ?? defaultImportModule)(addModuleVersion(manifest.entry,manifest.version));
        if(host.signal.aborted)throw new DOMException("Module initialization was cancelled.","AbortError");
        if ("initialize" in module) {initializing = true;instance = await module.initialize(host);}
        else {
          if (manifest.capabilities.includes("host-initialization")) throw new Error("The host extension has no initialization factory.");
          instance = module;
        }
        if (host.signal.aborted) throw new DOMException("Module initialization was cancelled.","AbortError");
        if (typeof instance.register !== "function") throw new Error("The module instance has no registration callback.");
        instances.push({manifest,instance});
      } catch (error) {
        if(instance)disposeStudioModuleInstance(instance);
        if (host.signal.aborted || initializing || manifest.capabilities.includes("host-initialization")) throw error;
        diagnostics.push({moduleId:manifest.id,status:"failed",reason:error instanceof Error ? error.message : String(error)});
      }
    }
    if (instances.filter(item => item.instance.connection).length > 1)
      throw new Error("Only one Studio connection provider may be active.");
    return {instances,diagnostics};
  } catch (error) {
    for (const {instance} of instances.reverse()) disposeStudioModuleInstance(instance);
    throw error;
  }
}

export async function registerStudioModuleInstances(instances: readonly StudioLoadedModule[], api: ElsaStudioModuleApi) {
  for (const {manifest,instance} of instances) {
    try {
      await instance.register(api);
      for (const contribution of instance.shell ?? []) api.shell?.add(contribution);
      addDiagnostic(api,manifest.id,"loaded","Module activated.");
    } catch (error) {
      if (instance.connection) throw error;
      addDiagnostic(api,manifest.id,"failed",error instanceof Error ? error.message : String(error));
    }
  }
}

export interface LoadModulesOptions {
  hostVersion: string;
  sdkVersion: string;
  importModule?: (entry: string) => Promise<ElsaStudioModule>;
  loadStyle?: (href: string) => Promise<void>;
}

export async function loadStudioModules(
  manifests: StudioModuleManifest[],
  api: ElsaStudioModuleApi,
  options: LoadModulesOptions
): Promise<StudioModuleDiagnostic[]> {
  const diagnostics: StudioModuleDiagnostic[] = [];
  const importModule = options.importModule ?? defaultImportModule;
  const loadStyle = options.loadStyle ?? defaultLoadStyle;

  for (const manifest of manifests) {
    if (!isVersionRangeCompatible(manifest.requiredHostVersion, options.hostVersion)) {
      diagnostics.push(addDiagnostic(api, manifest.id, "incompatible", `Requires host ${manifest.requiredHostVersion}.`));
      continue;
    }

    if (!isVersionRangeCompatible(manifest.requiredSdkVersion, options.sdkVersion)) {
      diagnostics.push(addDiagnostic(api, manifest.id, "incompatible", `Requires SDK ${manifest.requiredSdkVersion}.`));
      continue;
    }

    try {
      const moduleVersion = manifest.version;
      for (const style of manifest.styles) {
        await loadStyle(manifest.stylesHaveContentHashes === true ? style : addModuleVersion(style, moduleVersion));
      }

      const module = await importModule(addModuleVersion(manifest.entry, moduleVersion));
      if (!("register" in module)) throw new Error("This module requires the host initialization lifecycle.");
      await module.register(api);
      diagnostics.push(addDiagnostic(api, manifest.id, "loaded", "Module activated."));
    } catch (error) {
      diagnostics.push(addDiagnostic(api, manifest.id, "failed", error instanceof Error ? error.message : String(error)));
    }
  }

  return diagnostics;
}

export function isVersionRangeCompatible(range: string, actual: string): boolean {
  if (!range || range === "*" || range === actual) {
    return true;
  }

  if (range.startsWith("^")) {
    return major(range.slice(1)) === major(actual);
  }

  return false;
}

function major(version: string): string {
  return version.split(".")[0] ?? version;
}

export function addModuleVersion(asset: string, version: string): string {
  if (!asset || !version || asset.includes("studioModuleVersion=")) {
    return asset;
  }

  const hashIndex = asset.indexOf("#");
  const pathAndQuery = hashIndex === -1 ? asset : asset.slice(0, hashIndex);
  const hash = hashIndex === -1 ? "" : asset.slice(hashIndex);
  const separator = pathAndQuery.includes("?") ? "&" : "?";
  return `${pathAndQuery}${separator}studioModuleVersion=${encodeURIComponent(version)}${hash}`;
}

function addDiagnostic(
  api: ElsaStudioModuleApi,
  moduleId: string,
  status: StudioModuleDiagnostic["status"],
  reason: string
): StudioModuleDiagnostic {
  const diagnostic = { moduleId, status, reason };
  api.diagnostics.add(diagnostic);
  return diagnostic;
}

async function defaultImportModule(entry: string): Promise<ElsaStudioModule> {
  return import(/* @vite-ignore */ entry) as Promise<ElsaStudioModule>;
}

async function defaultLoadStyle(href: string): Promise<void> {
  if (document.querySelector(`link[href="${href}"]`)) {
    return;
  }

  await new Promise<void>((resolve, reject) => {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    link.onload = () => resolve();
    link.onerror = () => reject(new Error(`Failed to load stylesheet ${href}.`));
    document.head.appendChild(link);
  });
}
