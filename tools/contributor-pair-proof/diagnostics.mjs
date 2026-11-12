import { mkdir, rename, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { projectCreationState } from "./creation.mjs";

export const limits = Object.freeze({ captureMs: 3_000, events: 20, buttons: 3, jsonBytes: 16 * 1024, pngBytes: 2 * 1024 * 1024 });
const stages = new Set(["workflow-create", "workflow-root-selection", "workflow-root-card-visible", "workflow-root-card-click", "activity-picker-trigger-visible", "activity-picker-trigger-click"]);
const failureClasses = new Set(["timeout", "assertion", "ambiguous-locator", "navigation-interrupted", "error", "other"]);
const resourceTypes = new Set(["document", "stylesheet", "image", "media", "font", "script", "texttrack", "xhr", "fetch", "eventsource", "websocket", "manifest"]);

// Query values, fragments and arbitrary path segments never enter a receipt.
export function classifyLocation(rawUrl, studioOrigin) {
  try {
    const url = new URL(rawUrl);
    if (url.origin !== studioOrigin) return { location: "other-origin", editor: false };
    if (url.pathname !== "/workflows/definitions") return { location: "other-studio-route", editor: false };
    const definition = url.searchParams.has("definition");
    return { location: `${studioOrigin}/workflows/definitions${definition ? "?definition=[present]" : ""}`, editor: definition };
  } catch {
    return { location: "invalid-url", editor: false };
  }
}

function number(value, maximum = 1_000, minimum = 0) {
  return typeof value === "number" && Number.isFinite(value) ? Math.max(minimum, Math.min(maximum, Math.round(value * 100) / 100)) : null;
}

function choice(value, allowed) {
  return allowed.includes(value) ? value : "other";
}

function buttonState(value) {
  return {
    visible: value?.visible === true,
    enabled: value?.enabled === true,
    centerUnobstructed: value?.centerUnobstructed === true,
    coveringTag: choice(value?.coveringTag, ["BUTTON", "DIV", "SPAN", "SVG", "PATH", "BODY", "HTML", "DIALOG", "NONE"]),
    display: choice(value?.display, ["none", "block", "inline", "inline-block", "flex", "inline-flex", "grid", "contents"]),
    visibility: choice(value?.visibility, ["visible", "hidden", "collapse"]),
    pointerEvents: choice(value?.pointerEvents, ["auto", "none"]),
    opacity: number(value?.opacity, 1),
    bounds: Object.fromEntries(["x", "y", "width", "height"].map(key => [key, number(value?.bounds?.[key], 10_000, key === "x" || key === "y" ? -10_000 : 0)]))
  };
}

function networkLocation(rawUrl, studioOrigin) {
  try {
    const url = new URL(rawUrl);
    const origin = url.origin === studioOrigin ? "studio" : url.origin === "http://localhost:13000" ? "workbench" : "other";
    const route = /\.(?:js|css|wasm|woff2?)(?:$)/i.test(url.pathname) || url.pathname.startsWith("/_content/")
      ? "asset" : url.pathname.startsWith("/_elsa/") ? "api" : "other";
    return { origin, route };
  } catch {
    return { origin: "other", route: "other" };
  }
}

async function inspectPage(page) {
  const state = await page.evaluate(() => {
    const visible = element => {
      const style = getComputedStyle(element);
      return style.display !== "none" && style.visibility !== "hidden" && element.getClientRects().length > 0;
    };
    return {
      url: location.href,
      visibleDialogs: [...document.querySelectorAll('dialog, [role="dialog"]')].filter(visible).length,
      busyElements: document.querySelectorAll('[aria-busy="true"]').length,
      editorCanvases: document.querySelectorAll(".wf-canvas, .wf-designer, .wf-flowchart").length,
      viewport: { width: innerWidth, height: innerHeight }
    };
  });
  // Playwright's CSS locators also inspect open shadow roots. Do not mistake a
  // login form or embedded frame in one for a safe editor screenshot.
  state.sensitiveForms = await page.locator('input[type="password"], form[action*="/_elsa/identity/login"]').count();
  state.frames = await page.locator("iframe, frame").count();
  state.authFallback = await page.locator(".studio-auth-signing-in").count();
  return state;
}

function safeEditor(state, studioOrigin) {
  return classifyLocation(state?.url, studioOrigin).editor && state.sensitiveForms === 0 && state.frames === 0 && state.authFallback === 0;
}

// This observer never reads console text/arguments, error messages, request bodies,
// headers, cookies or storage. Every retained field is a fixed category or number.
export function observeEditor(page, { studioOrigin, outputDir, captureMs = limits.captureMs, creationState }) {
  const events = [];
  const listeners = [];
  let navigation = 0;
  let disposed = false;
  let unavailable = !path.isAbsolute(outputDir ?? "");
  const record = value => {
    events.push(value);
    if (events.length > limits.events) events.shift();
  };
  const listen = (name, project) => {
    const handler = value => {
      if (disposed) return;
      try {
        const projected = project(value);
        if (projected) record(projected);
      } catch {
        // Observation must never interrupt the journey or expose a raw exception.
      }
    };
    try { page.on(name, handler); listeners.push([name, handler]); } catch { unavailable = true; }
  };
  listen("console", message => {
    const type = message.type();
    return type === "error" ? { kind: "console-error" } : type === "warning" ? { kind: "console-warning" } : null;
  });
  listen("pageerror", error => ({ kind: "page-error", category: choice(error?.name, ["Error", "TypeError", "ReferenceError", "SyntaxError", "RangeError"]) }));
  listen("requestfailed", request => {
    const resource = request.resourceType();
    return { kind: "request-failed", ...networkLocation(request.url(), studioOrigin), resource: resourceTypes.has(resource) ? resource : "other" };
  });
  listen("response", response => {
    const status = response.status();
    return Number.isInteger(status) && status >= 400 && status <= 599
      ? { kind: "http-error", ...networkLocation(response.url(), studioOrigin), status } : null;
  });
  listen("framenavigated", frame => { if (frame === page.mainFrame()) navigation++; return null; });

  async function collect(stage, failureClass, active, signal) {
    let directoryCreated = false;
    const generation = navigation;
    let creationRejected = false;
    // Incomplete writes are never among the two files CI uploads. Abort is best
    // effort, so recheck the deadline after IO and remove any late publication.
    async function retain(name, contents, admissible = async () => true, unchanged = () => true) {
      const pending = path.join(outputDir, `${name}.pending`);
      const target = path.join(outputDir, name);
      try {
        await writeFile(pending, contents, { mode: 0o600, flag: "wx", signal });
        if (!active()) return false;
        if (!await admissible() || !active()) return false;
        await rename(pending, target);
        if (!active() || !unchanged()) { await rm(target, { force: true }); return false; }
        return true;
      } finally {
        await rm(pending, { force: true }).catch(() => {});
      }
    }
    try {
      await mkdir(outputDir, { mode: 0o700 });
      directoryCreated = true;
      if (!active()) return "capture-timeout";
      const before = await inspectPage(page);
      if (!active()) return "capture-timeout";
      const receipt = { schema: 1, stage, failure: failureClasses.has(failureClass) ? failureClass : "other", events: events.slice(), screenshot: "suppressed" };
      const safeCreationPage = state => {
        const location = classifyLocation(state.url, studioOrigin);
        return state.sensitiveForms === 0 && state.frames === 0 && state.authFallback === 0
          && (location.editor || location.location === `${studioOrigin}/workflows/definitions`);
      };
      if (typeof creationState === "function" && safeCreationPage(before)) {
        try { receipt.creation = projectCreationState(creationState()); } catch { receipt.creation = projectCreationState(null); }
      }
      if (!safeEditor(before, studioOrigin)) {
        receipt.capture = "suppressed-unsafe-page";
      } else {
        receipt.location = classifyLocation(before.url, studioOrigin).location;
        receipt.state = {
          visibleDialogs: number(before.visibleDialogs), busyElements: number(before.busyElements), editorCanvases: number(before.editorCanvases),
          viewport: { width: number(before.viewport?.width, 4_000), height: number(before.viewport?.height, 4_000) }
        };
        const buttons = page.getByRole("button", { name: "Add activity", exact: true, includeHidden: true });
        receipt.buttonCount = number(await buttons.count());
        const states = await buttons.evaluateAll((nodes, cap) => nodes.slice(0, cap).map(element => {
          const style = getComputedStyle(element);
          const rect = element.getBoundingClientRect();
          const covering = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
          return {
            visible: style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0,
            enabled: !element.disabled && element.getAttribute("aria-disabled") !== "true",
            centerUnobstructed: covering !== null && (covering === element || element.contains(covering)),
            coveringTag: covering?.tagName ?? "NONE", display: style.display, visibility: style.visibility,
            pointerEvents: style.pointerEvents, opacity: Number(style.opacity), bounds: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
          };
        }), limits.buttons);
        receipt.buttons = Array.isArray(states) ? states.slice(0, limits.buttons).map(buttonState) : [];
        if (!active()) return "capture-timeout";
        const preImage = await inspectPage(page);
        if (generation !== navigation || preImage.url !== before.url || !safeEditor(preImage, studioOrigin)) {
          creationRejected = true;
          receipt.capture = "suppressed-navigation-or-page-change";
        } else {
          try {
            const png = await page.screenshot({ type: "png", fullPage: false, timeout: 1_000, mask: [page.locator('input, textarea, [contenteditable]:not([contenteditable="false"])')], maskColor: "#000000" });
            if (!active()) return "capture-timeout";
            const after = await inspectPage(page);
            if (generation !== navigation || after.url !== before.url || !safeEditor(after, studioOrigin)) {
              creationRejected = true;
              receipt.capture = "suppressed-navigation-or-page-change";
            } else if (!Buffer.isBuffer(png) || png.length > limits.pngBytes || !png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) {
              receipt.capture = "screenshot-over-limit";
            } else {
              if (!active()) return "capture-timeout";
              if (!await retain("viewport.png", png)) return "capture-timeout";
              receipt.screenshot = "viewport.png";
              receipt.capture = "retained";
            }
          } catch {
            receipt.capture = "screenshot-unavailable";
          }
        }
      }
      if (!active()) return "capture-timeout";
      const admissibleCreation = async () => {
        if (creationRejected) return false;
        try {
          const current = await inspectPage(page);
          if (generation === navigation && current.url === before.url && safeCreationPage(current)) return true;
        } catch { /* An unavailable page cannot authorize creation evidence. */ }
        creationRejected = true;
        return false;
      };
      const creationUnmoved = () => {
        if (!creationRejected && generation === navigation) return true;
        creationRejected = true;
        return false;
      };
      const suppressCreation = async () => {
        delete receipt.creation;
        receipt.capture = "suppressed-navigation-or-page-change";
        if (receipt.screenshot === "viewport.png") await rm(path.join(outputDir, "viewport.png"), { force: true });
        receipt.screenshot = "suppressed";
      };
      if (receipt.creation && !await admissibleCreation()) await suppressCreation();
      const json = JSON.stringify(receipt, null, 2);
      if (Buffer.byteLength(json) > limits.jsonBytes) return "receipt-over-limit";
      // Recheck across filesystem awaits too. A rejected publication is replaced
      // once with the fixed summary, without creation metadata or screenshot.
      if (!await retain("diagnostic.json", json, receipt.creation ? admissibleCreation : undefined, receipt.creation ? creationUnmoved : undefined)) {
        if (!active() || !creationRejected) return "capture-timeout";
        await suppressCreation();
        if (!await retain("diagnostic.json", JSON.stringify(receipt, null, 2))) return "capture-timeout";
      }
      return receipt.capture;
    } catch {
      // The original error is owned by the caller. Diagnostic errors are fixed
      // classifications only, including filesystem and detached-page failures.
      return directoryCreated ? "capture-unavailable" : "directory-unavailable";
    }
  }

  return {
    async captureFailure(stage, failureClass) {
      if (!stages.has(stage)) return "not-applicable";
      if (unavailable) return "capture-unavailable";
      let active = true;
      let timer;
      const controller = new AbortController();
      try {
        return await Promise.race([
          collect(stage, failureClass, () => active, controller.signal),
          new Promise(resolve => { timer = setTimeout(() => { active = false; controller.abort(); resolve("capture-timeout"); }, Math.max(1, Math.min(captureMs, limits.captureMs))); })
        ]);
      } finally {
        active = false;
        controller.abort();
        clearTimeout(timer);
      }
    },
    dispose() {
      disposed = true;
      for (const [event, handler] of listeners) { try { page.off(event, handler); } catch {} }
    }
  };
}
