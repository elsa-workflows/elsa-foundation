import type {
  ElsaStudioModule,
  ElsaStudioModuleApi,
  StudioModuleDiagnostic,
  StudioModuleManifest
} from "../sdk";

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
      for (const style of manifest.styles) {
        await loadStyle(style);
      }

      const module = await importModule(manifest.entry);
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

