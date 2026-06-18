function y() {
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
function m(t, e = {}) {
  return {
    baseUrl: t,
    headers: e.headers,
    http: u(t, e.headers)
  };
}
function u(t, e) {
  return {
    async getJson(r, n) {
      return i(t, r, o(e, w(n)));
    },
    async postJson(r, n, s) {
      return i(t, r, o(e, {
        ...s,
        method: "POST",
        headers: g(s?.headers),
        body: JSON.stringify(n)
      }));
    }
  };
}
function o(t, e = {}) {
  return t ? {
    ...e,
    headers: h(t, e.headers)
  } : e;
}
function h(t, e) {
  const r = new Headers(t);
  return new Headers(e).forEach((n, s) => r.set(s, n)), r;
}
async function i(t, e, r) {
  const n = p(t, e), s = await fetch(n, r);
  if (!s.ok)
    throw new a(s.status, await f(s));
  const c = await s.text();
  try {
    return JSON.parse(c);
  } catch {
    throw new a(
      s.status,
      `Expected JSON from ${n}, but received ${l(s, c)}. Check Studio:BackendBaseUrl and make sure the backend maps this API route.`
    );
  }
}
async function f(t) {
  if ((t.headers.get("content-type") ?? "").includes("application/json"))
    try {
      const n = await t.json();
      return d(n) ?? `Request failed with ${t.status}.`;
    } catch {
      return `Request failed with ${t.status}.`;
    }
  return (await t.text()).trim() || `Request failed with ${t.status}.`;
}
function d(t) {
  if (typeof t.detail == "string" && t.detail.length > 0) return t.detail;
  if (typeof t.title == "string" && t.title.length > 0) return t.title;
  if (Array.isArray(t.errors) && t.errors.length > 0) return t.errors.map(String).join(" ");
  if (t.errors && typeof t.errors == "object") {
    const e = Object.values(t.errors).flatMap((r) => Array.isArray(r) ? r : [r]).map(String);
    if (e.length > 0) return e.join(" ");
  }
  return null;
}
function p(t, e) {
  return new URL(e, t).toString();
}
function w(t) {
  const e = new Headers(t?.headers);
  return e.has("Accept") || e.set("Accept", "application/json"), {
    ...t,
    cache: t?.cache ?? "no-store",
    headers: e
  };
}
function g(t) {
  const e = new Headers(t);
  return e.has("Content-Type") || e.set("Content-Type", "application/json"), e.has("Accept") || e.set("Accept", "application/json"), e;
}
function l(t, e) {
  const r = t.headers.get("content-type") ?? "an unknown content type", n = e.trim(), s = n.length > 0 ? `: ${n.slice(0, 80)}` : "";
  return `${r}${s}`;
}
class a extends Error {
  constructor(e, r) {
    super(r), this.status = e, this.name = "StudioHttpError";
  }
  status;
}
export {
  a as StudioHttpError,
  y as createContributionRegistry,
  m as createEndpointContext,
  u as createHttpClient,
  o as withDefaultHeaders
};
