function g() {
  const t = [];
  return {
    add(e) {
      t.push(e);
    },
    list() {
      return [...t];
    }
  };
}
function w(t) {
  return {
    baseUrl: t,
    http: u(t)
  };
}
function u(t) {
  return {
    async getJson(e, n) {
      return i(t, e, p(n));
    },
    async postJson(e, n, r) {
      return i(t, e, {
        ...r,
        method: "POST",
        headers: d(r?.headers),
        body: JSON.stringify(n)
      });
    }
  };
}
async function i(t, e, n) {
  const r = f(t, e), s = await fetch(r, n);
  if (!s.ok)
    throw new c(s.status, await a(s));
  const o = await s.text();
  try {
    return JSON.parse(o);
  } catch {
    throw new c(
      s.status,
      `Expected JSON from ${r}, but received ${l(s, o)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function a(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const r = await t.json();
      return h(r) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function h(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((n) => Array.isArray(n) ? n : [n]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function f(t, e) {
  return new URL(e, t).toString();
}
function p(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function d(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function l(t, e) {
  const n = t.headers.get("content-type") ?? "an unknown content type", r = e.trim(), s = r.length > 0 ? `: ${r.slice(0, 80)}` : "";
  return `${n}${s}`;
}
class c extends Error {
  constructor(e, n) {
    super(n), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  c as StudioHttpError,
  g as createContributionRegistry,
  w as createEndpointContext,
  u as createHttpClient
};
