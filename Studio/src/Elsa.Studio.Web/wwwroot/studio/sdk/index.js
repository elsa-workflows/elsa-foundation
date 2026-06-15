function u() {
  const t = [];
  return {
    add(n) {
      t.push(n);
    },
    list() {
      return [...t];
    }
  };
}
function s(t) {
  return {
    baseUrl: t,
    http: o(t)
  };
}
function o(t) {
  return {
    async getJson(n, r) {
      const e = await fetch(i(t, n), r);
      if (!e.ok)
        throw new Error(`Request failed with ${e.status}.`);
      return await e.json();
    }
  };
}
function i(t, n) {
  return new URL(n, t).toString();
}
export {
  u as createContributionRegistry,
  s as createEndpointContext,
  o as createHttpClient
};
