import { createHash } from "node:crypto";
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

// Bounded, owned-fixture UI evidence; this is not a sanitizer for arbitrary web pages or native traces.
const publicDraftValues = new Set(["studio-company", "Studio company", "Studio company edited", "unattached-retained-proof",
  "invalid key", "first-fixture", "First fixture", "pending-fixture-user", "no-such-fixture-user", "no-such-connection"]);
export function safeAria(value, forbidden = []) {
  if (typeof value !== "string" || value.length > 65_536) throw new Error("UI snapshot exceeded its bound.");
  if (forbidden.some(secret => secret && value.includes(secret)) ||
      /(?:\b(?:access_token|refresh_token|id_token|code_verifier|externalPreviewHandle)\b|eyJ[A-Za-z0-9_-]{20,}\.|-----BEGIN|Password123!)/u.test(value))
    throw new Error("UI snapshot contains excluded authentication material.");
  return value.replace(/https?:\/\/(?:localhost|127\.0\.0\.1|\[::1\])(?::\d+)?/gu, "[owned-origin]")
    .replace(/^(\s*- (?:textbox|searchbox|spinbutton|combobox)[^:\n]*):([^\n]*)/gmu, (_line, control, raw) => {
      const value = raw.trim();
      return `${control}: ${publicDraftValues.has(value) || [...publicDraftValues].some(item => JSON.stringify(item) === value) ? value : "[value omitted]"}`;
    });
}

export function checkedRoute(value, origins, blankLogin = false) {
  const url = new URL(value);
  const routes = ["/security/external-sign-in", "/security/user-access", "/backend/__tests__/live.html"];
  if (blankLogin) routes.push("/backend/_elsa/identity/login");
  if (!origins.includes(url.origin) || url.username || url.password || !routes.includes(url.pathname) || url.hash ||
      [...url.searchParams.keys()].some(key => !["backend", "view", ...(blankLogin ? ["returnUrl"] : [])].includes(key)))
    throw new Error("UI recording is disabled on this route.");
  for (const [key, item] of url.searchParams) {
    if (key === "backend" && item !== "owned" || key === "view" && item !== "access" ||
        key === "returnUrl" && !["/backend/__probe", "/backend/resource"].includes(item))
      throw new Error("UI recording query is outside the owned fixture.");
  }
  if (blankLogin && url.pathname !== "/backend/_elsa/identity/login") throw new Error("Chooser recording requires the login route.");
  return url.pathname;
}

export function createUiEvidence({ root, origins, forbidden = [], sourceSha, dirty }) {
  if (!/^[0-9a-f]{40}$/u.test(sourceSha) || typeof dirty !== "boolean") throw new Error("UI evidence requires actual source identity.");
  const privateRoot = mkdtempSync(join(tmpdir(), "elsa-3579-ui-private-"));
  chmodSync(privateRoot, 0o700);
  writeFileSync(join(root, ".ui-private-evidence"), privateRoot, { mode: 0o600 });
  const output = join(root, "ui-evidence"); mkdirSync(output, { recursive: true, mode: 0o700 });
  const files = []; let sequence = 0;
  return {
    async capture(page, name, { region = ".external-auth-shell:visible", blankLogin = false, action = "checkpoint" } = {}) {
      if (!/^[a-z0-9-]{1,80}$/u.test(name) || !/^[a-z0-9-]{1,80}$/u.test(action)) throw new Error("Invalid evidence checkpoint.");
      const path = checkedRoute(page.url(), origins, blankLogin);
      if (!await page.locator("input[type=password]").evaluateAll(inputs => inputs.every(input => input.value === "")))
        throw new Error("UI recording is disabled during credential entry.");
      if (blankLogin && !await page.locator("input[name=username], input[name=password]").evaluateAll(inputs => inputs.every(input => input.value === "")))
        throw new Error("Chooser recording requires blank credentials.");
      const selected = page.locator(region);
      if (await selected.count() !== 1) throw new Error("UI snapshot requires exactly one owned region.");
      const aria = await selected.ariaSnapshot();
      const id = `${String(++sequence).padStart(3, "0")}-${name}`;
      const snapshot = safeAria(aria, forbidden);
      writeFileSync(join(privateRoot, `${id}.aria.txt`), aria, { mode: 0o600 });
      const focus = await page.evaluate(() => {
        const element = globalThis.document.activeElement;
        return element ? { tag: element.tagName.toLowerCase(), id: element.id.slice(0, 160), role: element.getAttribute("role"),
          label: (element.getAttribute("aria-label") ?? ([...(element.labels ?? [])].map(label => label.textContent).join(" ") ||
            (element.tagName === "BUTTON" || element.getAttribute("role") === "button" ? element.textContent ?? "" : ""))).trim().slice(0, 160) } : null;
      });
      safeAria(JSON.stringify(focus), forbidden);
      const invalidFields = await selected.locator("input").evaluateAll(inputs => inputs.filter(input => !input.validity.valid)
        .map(input => ({ id: input.id.slice(0, 160), valueMissing: input.validity.valueMissing, typeMismatch: input.validity.typeMismatch,
          patternMismatch: input.validity.patternMismatch })));
      const noHorizontalOverflow = await page.evaluate(() => globalThis.document.documentElement.scrollWidth <= globalThis.innerWidth);
      const image = `ux-${id}.png`;
      const imageBytes = await selected.screenshot({ path: join(root, image), animations: "disabled" });
      const imageSha256 = createHash("sha256").update(imageBytes).digest("hex");
      const record = { sourceSha, dirty, sequence, name, action, path, focus, invalidFields, viewport: page.viewportSize(), noHorizontalOverflow, aria: snapshot, image, imageSha256,
        kind: "selected-aria-and-named-action", observation: "ARIA, focus and image are sequential observations", nativeReplay: false };
      const body = JSON.stringify(record, null, 2); const file = `${id}.json`;
      writeFileSync(join(output, file), body, { mode: 0o600 });
      files.push({ file: `ui-evidence/${file}`, sha256: createHash("sha256").update(body).digest("hex"), name, action, image, imageSha256 });
      if (!noHorizontalOverflow) throw new Error("Custom UI has horizontal page overflow; safe evidence retained.");
    },
    result(required = []) {
      for (const name of required) if (!files.some(file => file.name === name)) throw new Error("Required custom evidence checkpoint missing.");
      for (const file of files) {
        if (createHash("sha256").update(readFileSync(join(root, file.file))).digest("hex") !== file.sha256 ||
            createHash("sha256").update(readFileSync(join(root, file.image))).digest("hex") !== file.imageSha256)
          throw new Error("Custom evidence artifact identity changed.");
      }
      return { kind: "selected-aria-and-named-action", nativeReplay: "unverified", required: [...required], files: [...files] };
    }
  };
}
