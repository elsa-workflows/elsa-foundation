import { r as p } from "./chunks/index.js";
import { j as bt } from "./chunks/jsx-runtime.js";
var I = class {
  constructor() {
    this.listeners = /* @__PURE__ */ new Set(), this.subscribe = this.subscribe.bind(this);
  }
  subscribe(t) {
    return this.listeners.add(t), this.onSubscribe(), () => {
      this.listeners.delete(t), this.onUnsubscribe();
    };
  }
  hasListeners() {
    return this.listeners.size > 0;
  }
  onSubscribe() {
  }
  onUnsubscribe() {
  }
}, Nt = class extends I {
  #e;
  #t;
  #s;
  constructor() {
    super(), this.#s = (t) => {
      if (typeof window < "u" && window.addEventListener) {
        const e = () => t();
        return window.addEventListener("visibilitychange", e, !1), () => {
          window.removeEventListener("visibilitychange", e);
        };
      }
    };
  }
  onSubscribe() {
    this.#t || this.setEventListener(this.#s);
  }
  onUnsubscribe() {
    this.hasListeners() || (this.#t?.(), this.#t = void 0);
  }
  setEventListener(t) {
    this.#s = t, this.#t?.(), this.#t = t((e) => {
      typeof e == "boolean" ? this.setFocused(e) : this.onFocus();
    });
  }
  setFocused(t) {
    this.#e !== t && (this.#e = t, this.onFocus());
  }
  onFocus() {
    const t = this.isFocused();
    this.listeners.forEach((e) => {
      e(t);
    });
  }
  isFocused() {
    return typeof this.#e == "boolean" ? this.#e : globalThis.document?.visibilityState !== "hidden";
  }
}, et = new Nt(), Ht = {
  // We need the wrapper function syntax below instead of direct references to
  // global setTimeout etc.
  //
  // BAD: `setTimeout: setTimeout`
  // GOOD: `setTimeout: (cb, delay) => setTimeout(cb, delay)`
  //
  // If we use direct references here, then anything that wants to spy on or
  // replace the global setTimeout (like tests) won't work since we'll already
  // have a hard reference to the original implementation at the time when this
  // file was imported.
  setTimeout: (t, e) => setTimeout(t, e),
  clearTimeout: (t) => clearTimeout(t),
  setInterval: (t, e) => setInterval(t, e),
  clearInterval: (t) => clearInterval(t)
}, Bt = class {
  // We cannot have TimeoutManager<T> as we must instantiate it with a concrete
  // type at app boot; and if we leave that type, then any new timer provider
  // would need to support the default provider's concrete timer ID, which is
  // infeasible across environments.
  //
  // We settle for type safety for the TimeoutProvider type, and accept that
  // this class is unsafe internally to allow for extension.
  #e = Ht;
  #t = !1;
  setTimeoutProvider(t) {
    this.#e = t;
  }
  setTimeout(t, e) {
    return this.#e.setTimeout(t, e);
  }
  clearTimeout(t) {
    this.#e.clearTimeout(t);
  }
  setInterval(t, e) {
    return this.#e.setInterval(t, e);
  }
  clearInterval(t) {
    this.#e.clearInterval(t);
  }
}, D = new Bt();
function zt(t) {
  setTimeout(t, 0);
}
var Gt = typeof window > "u" || "Deno" in globalThis;
function S() {
}
function Wt(t, e) {
  return typeof t == "function" ? t(e) : t;
}
function W(t) {
  return typeof t == "number" && t >= 0 && t !== 1 / 0;
}
function Ot(t, e) {
  return Math.max(t + (e || 0) - Date.now(), 0);
}
function F(t, e) {
  return typeof t == "function" ? t(e) : t;
}
function R(t, e) {
  return typeof t == "function" ? t(e) : t;
}
function ut(t, e) {
  const {
    type: s = "all",
    exact: r,
    fetchStatus: i,
    predicate: n,
    queryKey: a,
    stale: h
  } = t;
  if (a) {
    if (r) {
      if (e.queryHash !== st(a, e.options))
        return !1;
    } else if (!A(e.queryKey, a))
      return !1;
  }
  if (s !== "all") {
    const o = e.isActive();
    if (s === "active" && !o || s === "inactive" && o)
      return !1;
  }
  return !(typeof h == "boolean" && e.isStale() !== h || i && i !== e.state.fetchStatus || n && !n(e));
}
function ht(t, e) {
  const { exact: s, status: r, predicate: i, mutationKey: n } = t;
  if (n) {
    if (!e.options.mutationKey)
      return !1;
    if (s) {
      if (T(e.options.mutationKey) !== T(n))
        return !1;
    } else if (!A(e.options.mutationKey, n))
      return !1;
  }
  return !(r && e.state.status !== r || i && !i(e));
}
function st(t, e) {
  return (e?.queryKeyHashFn || T)(t);
}
function T(t) {
  return JSON.stringify(
    t,
    (e, s) => $(s) ? Object.keys(s).sort().reduce((r, i) => (r[i] = s[i], r), {}) : s
  );
}
function A(t, e) {
  return t === e ? !0 : typeof t != typeof e ? !1 : t && e && typeof t == "object" && typeof e == "object" ? Object.keys(e).every((s) => A(t[s], e[s])) : !1;
}
var $t = Object.prototype.hasOwnProperty;
function z(t, e, s = 0) {
  if (t === e)
    return t;
  if (s > 500) return e;
  const r = ct(t) && ct(e);
  if (!r && !($(t) && $(e))) return e;
  const n = (r ? t : Object.keys(t)).length, a = r ? e : Object.keys(e), h = a.length, o = r ? new Array(h) : {};
  let d = 0;
  for (let u = 0; u < h; u++) {
    const l = r ? u : a[u], c = t[l], y = e[l];
    if (c === y) {
      o[l] = c, (r ? u < n : $t.call(t, l)) && d++;
      continue;
    }
    if (c === null || y === null || typeof c != "object" || typeof y != "object") {
      o[l] = y;
      continue;
    }
    const g = z(c, y, s + 1);
    o[l] = g, g === c && d++;
  }
  return n === h && d === n ? t : o;
}
function q(t, e) {
  if (!e || Object.keys(t).length !== Object.keys(e).length)
    return !1;
  for (const s in t)
    if (t[s] !== e[s])
      return !1;
  return !0;
}
function ct(t) {
  return Array.isArray(t) && t.length === Object.keys(t).length;
}
function $(t) {
  if (!lt(t))
    return !1;
  const e = t.constructor;
  if (e === void 0)
    return !0;
  const s = e.prototype;
  return !(!lt(s) || !s.hasOwnProperty("isPrototypeOf") || Object.getPrototypeOf(t) !== Object.prototype);
}
function lt(t) {
  return Object.prototype.toString.call(t) === "[object Object]";
}
function Vt(t) {
  return new Promise((e) => {
    D.setTimeout(e, t);
  });
}
function V(t, e, s) {
  return typeof s.structuralSharing == "function" ? s.structuralSharing(t, e) : s.structuralSharing !== !1 ? z(t, e) : e;
}
function Pe(t) {
  return t;
}
function St(t, e, s = 0) {
  const r = [...t, e];
  return s && r.length > s ? r.slice(1) : r;
}
function Jt(t, e, s = 0) {
  const r = [e, ...t];
  return s && r.length > s ? r.slice(0, -1) : r;
}
var rt = /* @__PURE__ */ Symbol();
function Rt(t, e) {
  return !t.queryFn && e?.initialPromise ? () => e.initialPromise : !t.queryFn || t.queryFn === rt ? () => Promise.reject(new Error(`Missing queryFn: '${t.queryHash}'`)) : t.queryFn;
}
function it(t, e) {
  return typeof t == "function" ? t(...e) : !!t;
}
function wt(t, e, s) {
  let r = !1, i;
  return Object.defineProperty(t, "signal", {
    enumerable: !0,
    get: () => (i ??= e(), r || (r = !0, i.aborted ? s() : i.addEventListener("abort", s, { once: !0 })), i)
  }), t;
}
var U = /* @__PURE__ */ (() => {
  let t = () => Gt;
  return {
    /**
     * Returns whether the current runtime should be treated as a server environment.
     */
    isServer() {
      return t();
    },
    /**
     * Overrides the server check globally.
     */
    setIsServer(e) {
      t = e;
    }
  };
})();
function J() {
  let t, e;
  const s = new Promise((i, n) => {
    t = i, e = n;
  });
  s.status = "pending", s.catch(() => {
  });
  function r(i) {
    Object.assign(s, i), delete s.resolve, delete s.reject;
  }
  return s.resolve = (i) => {
    r({
      status: "fulfilled",
      value: i
    }), t(i);
  }, s.reject = (i) => {
    r({
      status: "rejected",
      reason: i
    }), e(i);
  }, s;
}
function Zt(t) {
  let e;
  if (t.then((s) => (e = s, s), S)?.catch(S), e !== void 0)
    return { data: e };
}
function Pt(t) {
  return t;
}
function Xt(t) {
  return {
    mutationKey: t.options.mutationKey,
    state: t.state,
    ...t.options.scope && { scope: t.options.scope },
    ...t.meta && { meta: t.meta }
  };
}
function Yt(t, e, s) {
  const r = () => {
    const i = t.promise?.then(e).catch((n) => s(n) ? Promise.reject(new Error("redacted")) : Promise.reject(n));
    return i?.catch(S), i;
  };
  return {
    dehydratedAt: Date.now(),
    state: {
      ...t.state,
      ...t.state.data !== void 0 && {
        data: e(t.state.data)
      }
    },
    queryKey: t.queryKey,
    queryHash: t.queryHash,
    ...t.state.status === "pending" && {
      promise: r()
    },
    ...t.meta && { meta: t.meta },
    ...t.queryType && { queryType: t.queryType }
  };
}
function te(t) {
  return t.state.isPaused;
}
function ee(t) {
  return t.state.status === "success";
}
function se(t) {
  return !0;
}
function Ce(t, e = {}) {
  const s = e.shouldDehydrateMutation ?? t.getDefaultOptions().dehydrate?.shouldDehydrateMutation ?? te, r = t.getMutationCache().getAll().flatMap(
    (o) => s(o) ? [Xt(o)] : []
  ), i = e.shouldDehydrateQuery ?? t.getDefaultOptions().dehydrate?.shouldDehydrateQuery ?? ee, n = e.shouldRedactErrors ?? t.getDefaultOptions().dehydrate?.shouldRedactErrors ?? se, a = e.serializeData ?? t.getDefaultOptions().dehydrate?.serializeData ?? Pt, h = t.getQueryCache().getAll().flatMap(
    (o) => i(o) ? [Yt(o, a, n)] : []
  );
  return { mutations: r, queries: h };
}
function ft(t, e, s) {
  if (typeof e != "object" || e === null)
    return;
  const r = t.getMutationCache(), i = t.getQueryCache(), n = s?.defaultOptions?.deserializeData ?? t.getDefaultOptions().hydrate?.deserializeData ?? Pt, a = e.mutations || [], h = e.queries || [];
  a.forEach(({ state: o, ...d }) => {
    r.build(
      t,
      {
        ...t.getDefaultOptions().hydrate?.mutations,
        ...s?.defaultOptions?.mutations,
        ...d
      },
      o
    );
  }), h.forEach(
    ({
      queryKey: o,
      state: d,
      queryHash: u,
      meta: l,
      promise: c,
      dehydratedAt: y,
      queryType: g
    }) => {
      const f = c ? Zt(c) : void 0, v = d.data === void 0 ? f?.data : d.data, m = v === void 0 ? v : n(v);
      let O = i.get(u);
      const P = O?.state.status === "pending", C = O?.state.fetchStatus === "fetching";
      if (O) {
        const E = f && // We only need this undefined check to handle older dehydration
        // payloads that might not have dehydratedAt
        y !== void 0 && y > O.state.dataUpdatedAt;
        if (d.dataUpdatedAt > O.state.dataUpdatedAt || E) {
          const { fetchStatus: k, ...Q } = d;
          O.setState({
            ...Q,
            data: m,
            // If the query was pending at the moment of dehydration, but resolved to have data
            // before hydration, we can assume the query should be hydrated as successful.
            //
            // Since you can opt into dehydrating failed queries, and those can have data from
            // previous successful fetches, we make sure we only do this for pending queries.
            ...d.status === "pending" && m !== void 0 && {
              status: "success",
              // Preserve existing fetchStatus if the existing query is actively fetching.
              ...!C && {
                fetchStatus: "idle"
              }
            }
          });
        }
      } else
        O = i.build(
          t,
          {
            ...t.getDefaultOptions().hydrate?.queries,
            ...s?.defaultOptions?.queries,
            queryKey: o,
            queryHash: u,
            meta: l,
            _type: g
          },
          // Reset fetch status to idle to avoid
          // query being stuck in fetching state upon hydration
          {
            ...d,
            data: m,
            fetchStatus: "idle",
            // Like above, if the query was pending at the moment of dehydration but has data,
            // we can assume it should be hydrated as successful.
            status: d.status === "pending" && m !== void 0 ? "success" : d.status
          }
        );
      c && // If the data was synchronously available, there is no need to set up
      // a retryer and thus no reason to call fetch
      !f && !P && !C && // Only hydrate if dehydration is newer than any existing data,
      // this is always true for new queries
      (y === void 0 || y > O.state.dataUpdatedAt) && O.fetch(void 0, {
        // RSC transformed promises are not thenable
        initialPromise: Promise.resolve(c).then(n)
      }).catch(S);
    }
  );
}
var re = zt;
function ie() {
  let t = [], e = 0, s = (h) => {
    h();
  }, r = (h) => {
    h();
  }, i = re;
  const n = (h) => {
    e ? t.push(h) : i(() => {
      s(h);
    });
  }, a = () => {
    const h = t;
    t = [], h.length && i(() => {
      r(() => {
        h.forEach((o) => {
          s(o);
        });
      });
    });
  };
  return {
    batch: (h) => {
      let o;
      e++;
      try {
        o = h();
      } finally {
        e--, e || a();
      }
      return o;
    },
    /**
     * All calls to the wrapped function will be batched.
     */
    batchCalls: (h) => (...o) => {
      n(() => {
        h(...o);
      });
    },
    schedule: n,
    /**
     * Use this method to set a custom notify function.
     * This can be used to for example wrap notifications with `React.act` while running tests.
     */
    setNotifyFunction: (h) => {
      s = h;
    },
    /**
     * Use this method to set a custom function to batch notifications together into a single tick.
     * By default React Query will use the batch function provided by ReactDOM or React Native.
     */
    setBatchNotifyFunction: (h) => {
      r = h;
    },
    setScheduler: (h) => {
      i = h;
    }
  };
}
var b = ie(), ne = class extends I {
  #e = !0;
  #t;
  #s;
  constructor() {
    super(), this.#s = (t) => {
      if (typeof window < "u" && window.addEventListener) {
        const e = () => t(!0), s = () => t(!1);
        return window.addEventListener("online", e, !1), window.addEventListener("offline", s, !1), () => {
          window.removeEventListener("online", e), window.removeEventListener("offline", s);
        };
      }
    };
  }
  onSubscribe() {
    this.#t || this.setEventListener(this.#s);
  }
  onUnsubscribe() {
    this.hasListeners() || (this.#t?.(), this.#t = void 0);
  }
  setEventListener(t) {
    this.#s = t, this.#t?.(), this.#t = t(this.setOnline.bind(this));
  }
  setOnline(t) {
    this.#e !== t && (this.#e = t, this.listeners.forEach((s) => {
      s(t);
    }));
  }
  isOnline() {
    return this.#e;
  }
}, H = new ne();
function ae(t) {
  return Math.min(1e3 * 2 ** t, 3e4);
}
function Ct(t) {
  return (t ?? "online") === "online" ? H.isOnline() : !0;
}
var B = class extends Error {
  constructor(t) {
    super("CancelledError"), this.revert = t?.revert, this.silent = t?.silent;
  }
};
function Qe(t) {
  return t instanceof B;
}
function Qt(t) {
  let e = !1, s = 0, r;
  const i = J(), n = () => i.status !== "pending", a = (f) => {
    if (!n()) {
      const v = new B(f);
      c(v), t.onCancel?.(v);
    }
  }, h = () => {
    e = !0;
  }, o = () => {
    e = !1;
  }, d = () => et.isFocused() && (t.networkMode === "always" || H.isOnline()) && t.canRun(), u = () => Ct(t.networkMode) && t.canRun(), l = (f) => {
    n() || (r?.(), i.resolve(f));
  }, c = (f) => {
    n() || (r?.(), i.reject(f));
  }, y = () => new Promise((f) => {
    r = (v) => {
      (n() || d()) && f(v);
    }, t.onPause?.();
  }).then(() => {
    r = void 0, n() || t.onContinue?.();
  }), g = () => {
    if (n())
      return;
    let f;
    const v = s === 0 ? t.initialPromise : void 0;
    try {
      f = v ?? t.fn();
    } catch (m) {
      f = Promise.reject(m);
    }
    Promise.resolve(f).then(l).catch((m) => {
      if (n())
        return;
      const O = t.retry ?? (U.isServer() ? 0 : 3), P = t.retryDelay ?? ae, C = typeof P == "function" ? P(s, m) : P, E = O === !0 || typeof O == "number" && s < O || typeof O == "function" && O(s, m);
      if (e || !E) {
        c(m);
        return;
      }
      s++, t.onFail?.(s, m), Vt(C).then(() => d() ? void 0 : y()).then(() => {
        e ? c(m) : g();
      });
    });
  };
  return {
    promise: i,
    status: () => i.status,
    cancel: a,
    continue: () => (r?.(), i),
    cancelRetry: h,
    continueRetry: o,
    canStart: u,
    start: () => (u() ? g() : y().then(g), i)
  };
}
var Et = class {
  #e;
  destroy() {
    this.clearGcTimeout();
  }
  scheduleGc() {
    this.clearGcTimeout(), W(this.gcTime) && (this.#e = D.setTimeout(() => {
      this.optionalRemove();
    }, this.gcTime));
  }
  updateGcTime(t) {
    this.gcTime = Math.max(
      this.gcTime || 0,
      t ?? (U.isServer() ? 1 / 0 : 300 * 1e3)
    );
  }
  clearGcTimeout() {
    this.#e !== void 0 && (D.clearTimeout(this.#e), this.#e = void 0);
  }
};
function oe(t) {
  return {
    onFetch: (e, s) => {
      const r = e.options, i = e.fetchOptions?.meta?.fetchMore?.direction, n = e.state.data?.pages || [], a = e.state.data?.pageParams || [];
      let h = { pages: [], pageParams: [] }, o = 0;
      const d = async () => {
        let u = !1;
        const l = (g) => {
          wt(
            g,
            () => e.signal,
            () => u = !0
          );
        }, c = Rt(e.options, e.fetchOptions), y = async (g, f, v) => {
          if (u)
            return Promise.reject(e.signal.reason);
          if (f == null && g.pages.length)
            return Promise.resolve(g);
          const O = (() => {
            const k = {
              client: e.client,
              queryKey: e.queryKey,
              pageParam: f,
              direction: v ? "backward" : "forward",
              meta: e.options.meta
            };
            return l(k), k;
          })(), P = await c(O), { maxPages: C } = e.options, E = v ? Jt : St;
          return {
            pages: E(g.pages, P, C),
            pageParams: E(g.pageParams, f, C)
          };
        };
        if (i && n.length) {
          const g = i === "backward", f = g ? Mt : Z, v = {
            pages: n,
            pageParams: a
          }, m = f(r, v);
          h = await y(v, m, g);
        } else {
          const g = t ?? n.length;
          do {
            const f = o === 0 ? a[0] ?? r.initialPageParam : Z(r, h);
            if (o > 0 && f == null)
              break;
            h = await y(h, f), o++;
          } while (o < g);
        }
        return h;
      };
      e.options.persister ? e.fetchFn = () => e.options.persister?.(
        d,
        {
          client: e.client,
          queryKey: e.queryKey,
          meta: e.options.meta,
          signal: e.signal
        },
        s
      ) : e.fetchFn = d;
    }
  };
}
function Z(t, { pages: e, pageParams: s }) {
  const r = e.length - 1;
  return e.length > 0 ? t.getNextPageParam(
    e[r],
    e,
    s[r],
    s
  ) : void 0;
}
function Mt(t, { pages: e, pageParams: s }) {
  return e.length > 0 ? t.getPreviousPageParam?.(e[0], e, s[0], s) : void 0;
}
function ue(t, e) {
  return e ? Z(t, e) != null : !1;
}
function he(t, e) {
  return !e || !t.getPreviousPageParam ? !1 : Mt(t, e) != null;
}
var ce = class extends Et {
  #e;
  #t;
  #s;
  #r;
  #i;
  #n;
  #o;
  #a;
  constructor(t) {
    super(), this.#a = !1, this.#o = t.defaultOptions, this.setOptions(t.options), this.observers = [], this.#i = t.client, this.#r = this.#i.getQueryCache(), this.queryKey = t.queryKey, this.queryHash = t.queryHash, this.#t = pt(this.options), this.state = t.state ?? this.#t, this.scheduleGc();
  }
  get meta() {
    return this.options.meta;
  }
  get queryType() {
    return this.#e;
  }
  get promise() {
    return this.#n?.promise;
  }
  setOptions(t) {
    if (this.options = { ...this.#o, ...t }, t?._type && (this.#e = t._type), this.updateGcTime(this.options.gcTime), this.state && this.state.data === void 0) {
      const e = pt(this.options);
      e.data !== void 0 && (this.setState(
        dt(e.data, e.dataUpdatedAt)
      ), this.#t = e);
    }
  }
  optionalRemove() {
    !this.observers.length && this.state.fetchStatus === "idle" && this.#r.remove(this);
  }
  setData(t, e) {
    const s = V(this.state.data, t, this.options);
    return this.#u({
      data: s,
      type: "success",
      dataUpdatedAt: e?.updatedAt,
      manual: e?.manual
    }), s;
  }
  setState(t) {
    this.#u({ type: "setState", state: t });
  }
  cancel(t) {
    const e = this.#n?.promise;
    return this.#n?.cancel(t), e ? e.then(S).catch(S) : Promise.resolve();
  }
  destroy() {
    super.destroy(), this.cancel({ silent: !0 });
  }
  get resetState() {
    return this.#t;
  }
  reset() {
    this.destroy(), this.setState(this.resetState);
  }
  isActive() {
    return this.observers.some(
      (t) => R(t.options.enabled, this) !== !1
    );
  }
  isDisabled() {
    return this.getObserversCount() > 0 ? !this.isActive() : this.options.queryFn === rt || !this.isFetched();
  }
  isFetched() {
    return this.state.dataUpdateCount + this.state.errorUpdateCount > 0;
  }
  isStatic() {
    return this.getObserversCount() > 0 ? this.observers.some(
      (t) => F(t.options.staleTime, this) === "static"
    ) : !1;
  }
  isStale() {
    return this.getObserversCount() > 0 ? this.observers.some(
      (t) => t.getCurrentResult().isStale
    ) : this.state.data === void 0 || this.state.isInvalidated;
  }
  isStaleByTime(t = 0) {
    return this.state.data === void 0 ? !0 : t === "static" ? !1 : this.state.isInvalidated ? !0 : !Ot(this.state.dataUpdatedAt, t);
  }
  onFocus() {
    this.observers.find((e) => e.shouldFetchOnWindowFocus())?.refetch({ cancelRefetch: !1 }), this.#n?.continue();
  }
  onOnline() {
    this.observers.find((e) => e.shouldFetchOnReconnect())?.refetch({ cancelRefetch: !1 }), this.#n?.continue();
  }
  addObserver(t) {
    this.observers.includes(t) || (this.observers.push(t), this.clearGcTimeout(), this.#r.notify({ type: "observerAdded", query: this, observer: t }));
  }
  removeObserver(t) {
    this.observers.includes(t) && (this.observers = this.observers.filter((e) => e !== t), this.observers.length || (this.#n && (this.#a || this.#c() ? this.#n.cancel({ revert: !0 }) : this.#n.cancelRetry()), this.scheduleGc()), this.#r.notify({ type: "observerRemoved", query: this, observer: t }));
  }
  getObserversCount() {
    return this.observers.length;
  }
  #c() {
    return this.state.fetchStatus === "paused" && this.state.status === "pending";
  }
  invalidate() {
    this.state.isInvalidated || this.#u({ type: "invalidate" });
  }
  async fetch(t, e) {
    if (this.state.fetchStatus !== "idle" && // If the promise in the retryer is already rejected, we have to definitely
    // re-start the fetch; there is a chance that the query is still in a
    // pending state when that happens
    this.#n?.status() !== "rejected") {
      if (this.state.data !== void 0 && e?.cancelRefetch)
        this.cancel({ silent: !0 });
      else if (this.#n)
        return this.#n.continueRetry(), this.#n.promise;
    }
    if (t && this.setOptions(t), !this.options.queryFn) {
      const o = this.observers.find((d) => d.options.queryFn);
      o && this.setOptions(o.options);
    }
    const s = new AbortController(), r = (o) => {
      Object.defineProperty(o, "signal", {
        enumerable: !0,
        get: () => (this.#a = !0, s.signal)
      });
    }, i = () => {
      const o = Rt(this.options, e), u = (() => {
        const l = {
          client: this.#i,
          queryKey: this.queryKey,
          meta: this.meta
        };
        return r(l), l;
      })();
      return this.#a = !1, this.options.persister ? this.options.persister(
        o,
        u,
        this
      ) : o(u);
    }, a = (() => {
      const o = {
        fetchOptions: e,
        options: this.options,
        queryKey: this.queryKey,
        client: this.#i,
        state: this.state,
        fetchFn: i
      };
      return r(o), o;
    })();
    (this.#e === "infinite" ? oe(
      this.options.pages
    ) : this.options.behavior)?.onFetch(a, this), this.#s = this.state, (this.state.fetchStatus === "idle" || this.state.fetchMeta !== a.fetchOptions?.meta) && this.#u({ type: "fetch", meta: a.fetchOptions?.meta }), this.#n = Qt({
      initialPromise: e?.initialPromise,
      fn: a.fetchFn,
      onCancel: (o) => {
        o instanceof B && o.revert && this.setState({
          ...this.#s,
          fetchStatus: "idle"
        }), s.abort();
      },
      onFail: (o, d) => {
        this.#u({ type: "failed", failureCount: o, error: d });
      },
      onPause: () => {
        this.#u({ type: "pause" });
      },
      onContinue: () => {
        this.#u({ type: "continue" });
      },
      retry: a.options.retry,
      retryDelay: a.options.retryDelay,
      networkMode: a.options.networkMode,
      canRun: () => !0
    });
    try {
      const o = await this.#n.start();
      if (o === void 0)
        throw new Error(`${this.queryHash} data is undefined`);
      return this.setData(o), this.#r.config.onSuccess?.(o, this), this.#r.config.onSettled?.(
        o,
        this.state.error,
        this
      ), o;
    } catch (o) {
      if (o instanceof B) {
        if (o.silent)
          return this.#n.promise;
        if (o.revert) {
          if (this.state.data === void 0)
            throw o;
          return this.state.data;
        }
      }
      throw this.#u({
        type: "error",
        error: o
      }), this.#r.config.onError?.(
        o,
        this
      ), this.#r.config.onSettled?.(
        this.state.data,
        o,
        this
      ), o;
    } finally {
      this.scheduleGc();
    }
  }
  #u(t) {
    const e = (s) => {
      switch (t.type) {
        case "failed":
          return {
            ...s,
            fetchFailureCount: t.failureCount,
            fetchFailureReason: t.error
          };
        case "pause":
          return {
            ...s,
            fetchStatus: "paused"
          };
        case "continue":
          return {
            ...s,
            fetchStatus: "fetching"
          };
        case "fetch":
          return {
            ...s,
            ...Ft(s.data, this.options),
            fetchMeta: t.meta ?? null
          };
        case "success":
          const r = {
            ...s,
            ...dt(t.data, t.dataUpdatedAt),
            dataUpdateCount: s.dataUpdateCount + 1,
            ...!t.manual && {
              fetchStatus: "idle",
              fetchFailureCount: 0,
              fetchFailureReason: null
            }
          };
          return this.#s = t.manual ? r : void 0, r;
        case "error":
          const i = t.error;
          return {
            ...s,
            error: i,
            errorUpdateCount: s.errorUpdateCount + 1,
            errorUpdatedAt: Date.now(),
            fetchFailureCount: s.fetchFailureCount + 1,
            fetchFailureReason: i,
            fetchStatus: "idle",
            status: "error",
            // flag existing data as invalidated if we get a background error
            // note that "no data" always means stale so we can set unconditionally here
            isInvalidated: !0
          };
        case "invalidate":
          return {
            ...s,
            isInvalidated: !0
          };
        case "setState":
          return {
            ...s,
            ...t.state
          };
      }
    };
    this.state = e(this.state), b.batch(() => {
      this.observers.forEach((s) => {
        s.onQueryUpdate();
      }), this.#r.notify({ query: this, type: "updated", action: t });
    });
  }
};
function Ft(t, e) {
  return {
    fetchFailureCount: 0,
    fetchFailureReason: null,
    fetchStatus: Ct(e.networkMode) ? "fetching" : "paused",
    ...t === void 0 && {
      error: null,
      status: "pending"
    }
  };
}
function dt(t, e) {
  return {
    data: t,
    dataUpdatedAt: e ?? Date.now(),
    error: null,
    isInvalidated: !1,
    status: "success"
  };
}
function pt(t) {
  const e = typeof t.initialData == "function" ? t.initialData() : t.initialData, s = e !== void 0, r = s ? typeof t.initialDataUpdatedAt == "function" ? t.initialDataUpdatedAt() : t.initialDataUpdatedAt : 0;
  return {
    data: e,
    dataUpdateCount: 0,
    dataUpdatedAt: s ? r ?? Date.now() : 0,
    error: null,
    errorUpdateCount: 0,
    errorUpdatedAt: 0,
    fetchFailureCount: 0,
    fetchFailureReason: null,
    fetchMeta: null,
    isInvalidated: !1,
    status: s ? "success" : "pending",
    fetchStatus: "idle"
  };
}
var j = class extends I {
  constructor(t, e) {
    super(), this.options = e, this.#e = t, this.#a = null, this.#o = J(), this.bindMethods(), this.setOptions(e);
  }
  #e;
  #t = void 0;
  #s = void 0;
  #r = void 0;
  #i;
  #n;
  #o;
  #a;
  #c;
  #u;
  // This property keeps track of the last query with defined data.
  // It will be used to pass the previous data and query to the placeholder function between renders.
  #d;
  #l;
  #p;
  #h;
  #y = /* @__PURE__ */ new Set();
  bindMethods() {
    this.refetch = this.refetch.bind(this);
  }
  onSubscribe() {
    this.listeners.size === 1 && (this.#t.addObserver(this), yt(this.#t, this.options) ? this.#f() : this.updateResult(), this.#b());
  }
  onUnsubscribe() {
    this.hasListeners() || this.destroy();
  }
  shouldFetchOnReconnect() {
    return X(
      this.#t,
      this.options,
      this.options.refetchOnReconnect
    );
  }
  shouldFetchOnWindowFocus() {
    return X(
      this.#t,
      this.options,
      this.options.refetchOnWindowFocus
    );
  }
  destroy() {
    this.listeners = /* @__PURE__ */ new Set(), this.#O(), this.#S(), this.#t.removeObserver(this);
  }
  setOptions(t) {
    const e = this.options, s = this.#t;
    if (this.options = this.#e.defaultQueryOptions(t), this.options.enabled !== void 0 && typeof this.options.enabled != "boolean" && typeof this.options.enabled != "function" && typeof R(this.options.enabled, this.#t) != "boolean")
      throw new Error(
        "Expected enabled to be a boolean or a callback that returns a boolean"
      );
    this.#R(), this.#t.setOptions(this.options), e._defaulted && !q(this.options, e) && this.#e.getQueryCache().notify({
      type: "observerOptionsUpdated",
      query: this.#t,
      observer: this
    });
    const r = this.hasListeners();
    r && vt(
      this.#t,
      s,
      this.options,
      e
    ) && this.#f(), this.updateResult(), r && (this.#t !== s || R(this.options.enabled, this.#t) !== R(e.enabled, this.#t) || F(this.options.staleTime, this.#t) !== F(e.staleTime, this.#t)) && this.#v();
    const i = this.#m();
    r && (this.#t !== s || R(this.options.enabled, this.#t) !== R(e.enabled, this.#t) || i !== this.#h) && this.#g(i);
  }
  getOptimisticResult(t) {
    const e = this.#e.getQueryCache().build(this.#e, t), s = this.createResult(e, t);
    return fe(this, s) && (this.#r = s, this.#n = this.options, this.#i = this.#t.state), s;
  }
  getCurrentResult() {
    return this.#r;
  }
  trackResult(t, e) {
    return new Proxy(t, {
      get: (s, r) => (this.trackProp(r), e?.(r), r === "promise" && (this.trackProp("data"), !this.options.experimental_prefetchInRender && this.#o.status === "pending" && this.#o.reject(
        new Error(
          "experimental_prefetchInRender feature flag is not enabled"
        )
      )), Reflect.get(s, r))
    });
  }
  trackProp(t) {
    this.#y.add(t);
  }
  getCurrentQuery() {
    return this.#t;
  }
  refetch({ ...t } = {}) {
    return this.fetch({
      ...t
    });
  }
  fetchOptimistic(t) {
    const e = this.#e.defaultQueryOptions(t), s = this.#e.getQueryCache().build(this.#e, e);
    return s.fetch().then(() => this.createResult(s, e));
  }
  fetch(t) {
    return this.#f({
      ...t,
      cancelRefetch: t.cancelRefetch ?? !0
    }).then(() => (this.updateResult(), this.#r));
  }
  #f(t) {
    this.#R();
    let e = this.#t.fetch(
      this.options,
      t
    );
    return t?.throwOnError || (e = e.catch(S)), e;
  }
  #v() {
    this.#O();
    const t = F(
      this.options.staleTime,
      this.#t
    );
    if (U.isServer() || this.#r.isStale || !W(t))
      return;
    const s = Ot(this.#r.dataUpdatedAt, t) + 1;
    this.#l = D.setTimeout(() => {
      this.#r.isStale || this.updateResult();
    }, s);
  }
  #m() {
    return (typeof this.options.refetchInterval == "function" ? this.options.refetchInterval(this.#t) : this.options.refetchInterval) ?? !1;
  }
  #g(t) {
    this.#S(), this.#h = t, !(U.isServer() || R(this.options.enabled, this.#t) === !1 || !W(this.#h) || this.#h === 0) && (this.#p = D.setInterval(() => {
      (this.options.refetchIntervalInBackground || et.isFocused()) && this.#f();
    }, this.#h));
  }
  #b() {
    this.#v(), this.#g(this.#m());
  }
  #O() {
    this.#l !== void 0 && (D.clearTimeout(this.#l), this.#l = void 0);
  }
  #S() {
    this.#p !== void 0 && (D.clearInterval(this.#p), this.#p = void 0);
  }
  createResult(t, e) {
    const s = this.#t, r = this.options, i = this.#r, n = this.#i, a = this.#n, o = t !== s ? t.state : this.#s, { state: d } = t;
    let u = { ...d }, l = !1, c;
    if (e._optimisticResults) {
      const w = this.hasListeners(), x = !w && yt(t, e), K = w && vt(t, s, e, r);
      (x || K) && (u = {
        ...u,
        ...Ft(d.data, t.options)
      }), e._optimisticResults === "isRestoring" && (u.fetchStatus = "idle");
    }
    let { error: y, errorUpdatedAt: g, status: f } = u;
    c = u.data;
    let v = !1;
    if (e.placeholderData !== void 0 && c === void 0 && f === "pending") {
      let w;
      i?.isPlaceholderData && e.placeholderData === a?.placeholderData ? (w = i.data, v = !0) : w = typeof e.placeholderData == "function" ? e.placeholderData(
        this.#d?.state.data,
        this.#d
      ) : e.placeholderData, w !== void 0 && (f = "success", c = V(
        i?.data,
        w,
        e
      ), l = !0);
    }
    if (e.select && c !== void 0 && !v)
      if (i && c === n?.data && e.select === this.#c)
        c = this.#u;
      else
        try {
          this.#c = e.select, c = e.select(c), c = V(i?.data, c, e), this.#u = c, this.#a = null;
        } catch (w) {
          this.#a = w;
        }
    this.#a && (y = this.#a, c = this.#u, g = Date.now(), f = "error");
    const m = u.fetchStatus === "fetching", O = f === "pending", P = f === "error", C = O && m, E = c !== void 0, Q = {
      status: f,
      fetchStatus: u.fetchStatus,
      isPending: O,
      isSuccess: f === "success",
      isError: P,
      isInitialLoading: C,
      isLoading: C,
      data: c,
      dataUpdatedAt: u.dataUpdatedAt,
      error: y,
      errorUpdatedAt: g,
      failureCount: u.fetchFailureCount,
      failureReason: u.fetchFailureReason,
      errorUpdateCount: u.errorUpdateCount,
      isFetched: t.isFetched(),
      isFetchedAfterMount: u.dataUpdateCount > o.dataUpdateCount || u.errorUpdateCount > o.errorUpdateCount,
      isFetching: m,
      isRefetching: m && !O,
      isLoadingError: P && !E,
      isPaused: u.fetchStatus === "paused",
      isPlaceholderData: l,
      isRefetchError: P && E,
      isStale: nt(t, e),
      refetch: this.refetch,
      promise: this.#o,
      isEnabled: R(e.enabled, t) !== !1
    };
    if (this.options.experimental_prefetchInRender) {
      const w = Q.data !== void 0, x = Q.status === "error" && !w, K = (L) => {
        x ? L.reject(Q.error) : w && L.resolve(Q.data);
      }, ot = () => {
        const L = this.#o = Q.promise = J();
        K(L);
      }, _ = this.#o;
      switch (_.status) {
        case "pending":
          t.queryHash === s.queryHash && K(_);
          break;
        case "fulfilled":
          (x || Q.data !== _.value) && ot();
          break;
        case "rejected":
          (!x || Q.error !== _.reason) && ot();
          break;
      }
    }
    return Q;
  }
  updateResult() {
    const t = this.#r, e = this.createResult(this.#t, this.options);
    if (this.#i = this.#t.state, this.#n = this.options, this.#i.data !== void 0 && (this.#d = this.#t), q(e, t))
      return;
    this.#r = e;
    const s = () => {
      if (!t)
        return !0;
      const { notifyOnChangeProps: r } = this.options, i = typeof r == "function" ? r() : r;
      if (i === "all" || !i && !this.#y.size)
        return !0;
      const n = new Set(
        i ?? this.#y
      );
      return this.options.throwOnError && n.add("error"), Object.keys(this.#r).some((a) => {
        const h = a;
        return this.#r[h] !== t[h] && n.has(h);
      });
    };
    this.#w({ listeners: s() });
  }
  #R() {
    const t = this.#e.getQueryCache().build(this.#e, this.options);
    if (t === this.#t)
      return;
    const e = this.#t;
    this.#t = t, this.#s = t.state, this.hasListeners() && (e?.removeObserver(this), t.addObserver(this));
  }
  onQueryUpdate() {
    this.updateResult(), this.hasListeners() && this.#b();
  }
  #w(t) {
    b.batch(() => {
      t.listeners && this.listeners.forEach((e) => {
        e(this.#r);
      }), this.#e.getQueryCache().notify({
        query: this.#t,
        type: "observerResultsUpdated"
      });
    });
  }
};
function le(t, e) {
  return R(e.enabled, t) !== !1 && t.state.data === void 0 && !(t.state.status === "error" && R(e.retryOnMount, t) === !1);
}
function yt(t, e) {
  return le(t, e) || t.state.data !== void 0 && X(t, e, e.refetchOnMount);
}
function X(t, e, s) {
  if (R(e.enabled, t) !== !1 && F(e.staleTime, t) !== "static") {
    const r = typeof s == "function" ? s(t) : s;
    return r === "always" || r !== !1 && nt(t, e);
  }
  return !1;
}
function vt(t, e, s, r) {
  return (t !== e || R(r.enabled, t) === !1) && (!s.suspense || t.state.status !== "error") && nt(t, s);
}
function nt(t, e) {
  return R(e.enabled, t) !== !1 && t.isStaleByTime(F(e.staleTime, t));
}
function fe(t, e) {
  return !q(t.getCurrentResult(), e);
}
var Dt = class extends j {
  constructor(t, e) {
    super(t, e);
  }
  bindMethods() {
    super.bindMethods(), this.fetchNextPage = this.fetchNextPage.bind(this), this.fetchPreviousPage = this.fetchPreviousPage.bind(this);
  }
  setOptions(t) {
    t._type = "infinite", super.setOptions(t);
  }
  getOptimisticResult(t) {
    return t._type = "infinite", super.getOptimisticResult(t);
  }
  fetchNextPage(t) {
    return this.fetch({
      ...t,
      meta: {
        fetchMore: { direction: "forward" }
      }
    });
  }
  fetchPreviousPage(t) {
    return this.fetch({
      ...t,
      meta: {
        fetchMore: { direction: "backward" }
      }
    });
  }
  createResult(t, e) {
    const { state: s } = t, r = super.createResult(t, e), { isFetching: i, isRefetching: n, isError: a, isRefetchError: h } = r, o = s.fetchMeta?.fetchMore?.direction, d = a && o === "forward", u = i && o === "forward", l = a && o === "backward", c = i && o === "backward";
    return {
      ...r,
      fetchNextPage: this.fetchNextPage,
      fetchPreviousPage: this.fetchPreviousPage,
      hasNextPage: ue(e, s.data),
      hasPreviousPage: he(e, s.data),
      isFetchNextPageError: d,
      isFetchingNextPage: u,
      isFetchPreviousPageError: l,
      isFetchingPreviousPage: c,
      isRefetchError: h && !d && !l,
      isRefetching: n && !u && !c
    };
  }
}, de = class extends Et {
  #e;
  #t;
  #s;
  #r;
  constructor(t) {
    super(), this.#e = t.client, this.mutationId = t.mutationId, this.#s = t.mutationCache, this.#t = [], this.state = t.state || Tt(), this.setOptions(t.options), this.scheduleGc();
  }
  setOptions(t) {
    this.options = t, this.updateGcTime(this.options.gcTime);
  }
  get meta() {
    return this.options.meta;
  }
  addObserver(t) {
    this.#t.includes(t) || (this.#t.push(t), this.clearGcTimeout(), this.#s.notify({
      type: "observerAdded",
      mutation: this,
      observer: t
    }));
  }
  removeObserver(t) {
    this.#t = this.#t.filter((e) => e !== t), this.scheduleGc(), this.#s.notify({
      type: "observerRemoved",
      mutation: this,
      observer: t
    });
  }
  optionalRemove() {
    this.#t.length || (this.state.status === "pending" ? this.scheduleGc() : this.#s.remove(this));
  }
  continue() {
    return this.#r?.continue() ?? // continuing a mutation assumes that variables are set, mutation must have been dehydrated before
    this.execute(this.state.variables);
  }
  async execute(t) {
    const e = () => {
      this.#i({ type: "continue" });
    }, s = {
      client: this.#e,
      meta: this.options.meta,
      mutationKey: this.options.mutationKey
    };
    this.#r = Qt({
      fn: () => this.options.mutationFn ? this.options.mutationFn(t, s) : Promise.reject(new Error("No mutationFn found")),
      onFail: (n, a) => {
        this.#i({ type: "failed", failureCount: n, error: a });
      },
      onPause: () => {
        this.#i({ type: "pause" });
      },
      onContinue: e,
      retry: this.options.retry ?? 0,
      retryDelay: this.options.retryDelay,
      networkMode: this.options.networkMode,
      canRun: () => this.#s.canRun(this)
    });
    const r = this.state.status === "pending", i = !this.#r.canStart();
    try {
      if (r)
        e();
      else {
        this.#i({ type: "pending", variables: t, isPaused: i }), this.#s.config.onMutate && await this.#s.config.onMutate(
          t,
          this,
          s
        );
        const a = await this.options.onMutate?.(
          t,
          s
        );
        a !== this.state.context && this.#i({
          type: "pending",
          context: a,
          variables: t,
          isPaused: i
        });
      }
      const n = await this.#r.start();
      return await this.#s.config.onSuccess?.(
        n,
        t,
        this.state.context,
        this,
        s
      ), await this.options.onSuccess?.(
        n,
        t,
        this.state.context,
        s
      ), await this.#s.config.onSettled?.(
        n,
        null,
        this.state.variables,
        this.state.context,
        this,
        s
      ), await this.options.onSettled?.(
        n,
        null,
        t,
        this.state.context,
        s
      ), this.#i({ type: "success", data: n }), n;
    } catch (n) {
      try {
        await this.#s.config.onError?.(
          n,
          t,
          this.state.context,
          this,
          s
        );
      } catch (a) {
        Promise.reject(a);
      }
      try {
        await this.options.onError?.(
          n,
          t,
          this.state.context,
          s
        );
      } catch (a) {
        Promise.reject(a);
      }
      try {
        await this.#s.config.onSettled?.(
          void 0,
          n,
          this.state.variables,
          this.state.context,
          this,
          s
        );
      } catch (a) {
        Promise.reject(a);
      }
      try {
        await this.options.onSettled?.(
          void 0,
          n,
          t,
          this.state.context,
          s
        );
      } catch (a) {
        Promise.reject(a);
      }
      throw this.#i({ type: "error", error: n }), n;
    } finally {
      this.#s.runNext(this);
    }
  }
  #i(t) {
    const e = (s) => {
      switch (t.type) {
        case "failed":
          return {
            ...s,
            failureCount: t.failureCount,
            failureReason: t.error
          };
        case "pause":
          return {
            ...s,
            isPaused: !0
          };
        case "continue":
          return {
            ...s,
            isPaused: !1
          };
        case "pending":
          return {
            ...s,
            context: t.context,
            data: void 0,
            failureCount: 0,
            failureReason: null,
            error: null,
            isPaused: t.isPaused,
            status: "pending",
            variables: t.variables,
            submittedAt: Date.now()
          };
        case "success":
          return {
            ...s,
            data: t.data,
            failureCount: 0,
            failureReason: null,
            error: null,
            status: "success",
            isPaused: !1
          };
        case "error":
          return {
            ...s,
            data: void 0,
            error: t.error,
            failureCount: s.failureCount + 1,
            failureReason: t.error,
            isPaused: !1,
            status: "error"
          };
      }
    };
    this.state = e(this.state), b.batch(() => {
      this.#t.forEach((s) => {
        s.onMutationUpdate(t);
      }), this.#s.notify({
        mutation: this,
        type: "updated",
        action: t
      });
    });
  }
};
function Tt() {
  return {
    context: void 0,
    data: void 0,
    error: null,
    failureCount: 0,
    failureReason: null,
    isPaused: !1,
    status: "idle",
    variables: void 0,
    submittedAt: 0
  };
}
var pe = class extends I {
  constructor(t = {}) {
    super(), this.config = t, this.#e = /* @__PURE__ */ new Set(), this.#t = /* @__PURE__ */ new Map(), this.#s = 0;
  }
  #e;
  #t;
  #s;
  build(t, e, s) {
    const r = new de({
      client: t,
      mutationCache: this,
      mutationId: ++this.#s,
      options: t.defaultMutationOptions(e),
      state: s
    });
    return this.add(r), r;
  }
  add(t) {
    this.#e.add(t);
    const e = N(t);
    if (typeof e == "string") {
      const s = this.#t.get(e);
      s ? s.push(t) : this.#t.set(e, [t]);
    }
    this.notify({ type: "added", mutation: t });
  }
  remove(t) {
    if (this.#e.delete(t)) {
      const e = N(t);
      if (typeof e == "string") {
        const s = this.#t.get(e);
        if (s)
          if (s.length > 1) {
            const r = s.indexOf(t);
            r !== -1 && s.splice(r, 1);
          } else s[0] === t && this.#t.delete(e);
      }
    }
    this.notify({ type: "removed", mutation: t });
  }
  canRun(t) {
    const e = N(t);
    if (typeof e == "string") {
      const r = this.#t.get(e)?.find(
        (i) => i.state.status === "pending"
      );
      return !r || r === t;
    } else
      return !0;
  }
  runNext(t) {
    const e = N(t);
    return typeof e == "string" ? this.#t.get(e)?.find((r) => r !== t && r.state.isPaused)?.continue() ?? Promise.resolve() : Promise.resolve();
  }
  clear() {
    b.batch(() => {
      this.#e.forEach((t) => {
        this.notify({ type: "removed", mutation: t });
      }), this.#e.clear(), this.#t.clear();
    });
  }
  getAll() {
    return Array.from(this.#e);
  }
  find(t) {
    const e = { exact: !0, ...t };
    return this.getAll().find(
      (s) => ht(e, s)
    );
  }
  findAll(t = {}) {
    return this.getAll().filter((e) => ht(t, e));
  }
  notify(t) {
    b.batch(() => {
      this.listeners.forEach((e) => {
        e(t);
      });
    });
  }
  resumePausedMutations() {
    const t = this.getAll().filter((e) => e.state.isPaused);
    return b.batch(
      () => Promise.all(
        t.map((e) => e.continue().catch(S))
      )
    );
  }
};
function N(t) {
  return t.options.scope?.id;
}
var ye = class extends I {
  #e;
  #t = void 0;
  #s;
  #r;
  constructor(t, e) {
    super(), this.#e = t, this.setOptions(e), this.bindMethods(), this.#i();
  }
  bindMethods() {
    this.mutate = this.mutate.bind(this), this.reset = this.reset.bind(this);
  }
  setOptions(t) {
    const e = this.options;
    this.options = this.#e.defaultMutationOptions(t), q(this.options, e) || this.#e.getMutationCache().notify({
      type: "observerOptionsUpdated",
      mutation: this.#s,
      observer: this
    }), e?.mutationKey && this.options.mutationKey && T(e.mutationKey) !== T(this.options.mutationKey) ? this.reset() : this.#s?.state.status === "pending" && this.#s.setOptions(this.options);
  }
  onUnsubscribe() {
    this.hasListeners() || this.#s?.removeObserver(this);
  }
  onMutationUpdate(t) {
    this.#i(), this.#n(t);
  }
  getCurrentResult() {
    return this.#t;
  }
  reset() {
    this.#s?.removeObserver(this), this.#s = void 0, this.#i(), this.#n();
  }
  mutate(t, e) {
    return this.#r = e, this.#s?.removeObserver(this), this.#s = this.#e.getMutationCache().build(this.#e, this.options), this.#s.addObserver(this), this.#s.execute(t);
  }
  #i() {
    const t = this.#s?.state ?? Tt();
    this.#t = {
      ...t,
      isPending: t.status === "pending",
      isSuccess: t.status === "success",
      isError: t.status === "error",
      isIdle: t.status === "idle",
      mutate: this.mutate,
      reset: this.reset
    };
  }
  #n(t) {
    b.batch(() => {
      if (this.#r && this.hasListeners()) {
        const e = this.#t.variables, s = this.#t.context, r = {
          client: this.#e,
          meta: this.options.meta,
          mutationKey: this.options.mutationKey
        };
        if (t?.type === "success") {
          try {
            this.#r.onSuccess?.(
              t.data,
              e,
              s,
              r
            );
          } catch (i) {
            Promise.reject(i);
          }
          try {
            this.#r.onSettled?.(
              t.data,
              null,
              e,
              s,
              r
            );
          } catch (i) {
            Promise.reject(i);
          }
        } else if (t?.type === "error") {
          try {
            this.#r.onError?.(
              t.error,
              e,
              s,
              r
            );
          } catch (i) {
            Promise.reject(i);
          }
          try {
            this.#r.onSettled?.(
              void 0,
              t.error,
              e,
              s,
              r
            );
          } catch (i) {
            Promise.reject(i);
          }
        }
      }
      this.listeners.forEach((e) => {
        e(this.#t);
      });
    });
  }
};
function mt(t, e) {
  const s = new Set(e);
  return t.filter((r) => !s.has(r));
}
function ve(t, e, s) {
  const r = t.slice(0);
  return r[e] = s, r;
}
var me = class extends I {
  #e;
  #t;
  #s;
  #r;
  #i;
  #n;
  #o;
  #a;
  #c;
  #u = [];
  constructor(t, e, s) {
    super(), this.#e = t, this.#r = s, this.#s = [], this.#i = [], this.#t = [], this.setQueries(e);
  }
  onSubscribe() {
    this.listeners.size === 1 && this.#i.forEach((t) => {
      t.subscribe((e) => {
        this.#y(t, e);
      });
    });
  }
  onUnsubscribe() {
    this.listeners.size || this.destroy();
  }
  destroy() {
    this.listeners = /* @__PURE__ */ new Set(), this.#i.forEach((t) => {
      t.destroy();
    });
  }
  setQueries(t, e) {
    this.#s = t, this.#r = e, b.batch(() => {
      const s = this.#i, r = this.#h(this.#s);
      r.forEach(
        (u) => u.observer.setOptions(u.defaultedQueryOptions)
      );
      const i = r.map((u) => u.observer), n = i.map(
        (u) => u.getCurrentResult()
      ), a = s.length !== i.length, h = i.some(
        (u, l) => u !== s[l]
      ), o = a || h, d = o ? !0 : n.some((u, l) => {
        const c = this.#t[l];
        return !c || !q(u, c);
      });
      !o && !d || (o && (this.#u = r, this.#i = i), this.#t = n, this.hasListeners() && (o && (mt(s, i).forEach((u) => {
        u.destroy();
      }), mt(i, s).forEach((u) => {
        u.subscribe((l) => {
          this.#y(u, l);
        });
      })), this.#f()));
    });
  }
  getCurrentResult() {
    return this.#t;
  }
  getQueries() {
    return this.#i.map((t) => t.getCurrentQuery());
  }
  getObservers() {
    return this.#i;
  }
  getOptimisticResult(t, e) {
    const s = this.#h(t), r = s.map(
      (n) => n.observer.getOptimisticResult(n.defaultedQueryOptions)
    ), i = s.map(
      (n) => n.defaultedQueryOptions.queryHash
    );
    return [
      r,
      (n) => this.#l(n ?? r, e, i),
      () => this.#d(r, s)
    ];
  }
  #d(t, e) {
    return e.map((s, r) => {
      const i = t[r];
      return s.defaultedQueryOptions.notifyOnChangeProps ? i : s.observer.trackResult(i, (n) => {
        e.forEach((a) => {
          a.observer.trackProp(n);
        });
      });
    });
  }
  #l(t, e, s) {
    if (e) {
      const r = this.#c, i = s !== void 0 && r !== void 0 && (r.length !== s.length || s.some((n, a) => n !== r[a]));
      return (!this.#n || this.#t !== this.#a || i || e !== this.#o) && (this.#o = e, this.#a = this.#t, s !== void 0 && (this.#c = s), this.#n = z(
        this.#n,
        e(t)
      )), this.#n;
    }
    return t;
  }
  #p() {
    return this.#r?.combine !== void 0 && this.#i.some((t, e) => t.options.suspense && this.#t[e]?.data === void 0);
  }
  #h(t) {
    const e = /* @__PURE__ */ new Map();
    this.#i.forEach((r) => {
      const i = r.options.queryHash;
      if (!i) return;
      const n = e.get(i);
      n ? n.push(r) : e.set(i, [r]);
    });
    const s = [];
    return t.forEach((r) => {
      const i = this.#e.defaultQueryOptions(r), a = e.get(i.queryHash)?.shift() ?? new j(this.#e, i);
      s.push({
        defaultedQueryOptions: i,
        observer: a
      });
    }), s;
  }
  #y(t, e) {
    const s = this.#i.indexOf(t);
    s !== -1 && (this.#t = ve(this.#t, s, e), this.#f());
  }
  #f() {
    if (this.hasListeners()) {
      const t = this.#d(this.#t, this.#u), e = this.#p(), s = this.#n, r = e ? s : this.#l(t, this.#r?.combine);
      (e || s !== r) && b.batch(() => {
        this.listeners.forEach((i) => {
          i(this.#t);
        });
      });
    }
  }
}, ge = class extends I {
  constructor(t = {}) {
    super(), this.config = t, this.#e = /* @__PURE__ */ new Map();
  }
  #e;
  build(t, e, s) {
    const r = e.queryKey, i = e.queryHash ?? st(r, e);
    let n = this.get(i);
    return n || (n = new ce({
      client: t,
      queryKey: r,
      queryHash: i,
      options: t.defaultQueryOptions(e),
      state: s,
      defaultOptions: t.getQueryDefaults(r)
    }), this.add(n)), n;
  }
  add(t) {
    this.#e.has(t.queryHash) || (this.#e.set(t.queryHash, t), this.notify({
      type: "added",
      query: t
    }));
  }
  remove(t) {
    const e = this.#e.get(t.queryHash);
    e && (t.destroy(), e === t && this.#e.delete(t.queryHash), this.notify({ type: "removed", query: t }));
  }
  clear() {
    b.batch(() => {
      this.getAll().forEach((t) => {
        this.remove(t);
      });
    });
  }
  get(t) {
    return this.#e.get(t);
  }
  getAll() {
    return [...this.#e.values()];
  }
  find(t) {
    const e = { exact: !0, ...t };
    return this.getAll().find(
      (s) => ut(e, s)
    );
  }
  findAll(t = {}) {
    const e = this.getAll();
    return Object.keys(t).length > 0 ? e.filter((s) => ut(t, s)) : e;
  }
  notify(t) {
    b.batch(() => {
      this.listeners.forEach((e) => {
        e(t);
      });
    });
  }
  onFocus() {
    b.batch(() => {
      this.getAll().forEach((t) => {
        t.onFocus();
      });
    });
  }
  onOnline() {
    b.batch(() => {
      this.getAll().forEach((t) => {
        t.onOnline();
      });
    });
  }
}, Ee = class {
  #e;
  #t;
  #s;
  #r;
  #i;
  #n;
  #o;
  #a;
  constructor(t = {}) {
    this.#e = t.queryCache || new ge(), this.#t = t.mutationCache || new pe(), this.#s = t.defaultOptions || {}, this.#r = /* @__PURE__ */ new Map(), this.#i = /* @__PURE__ */ new Map(), this.#n = 0;
  }
  mount() {
    this.#n++, this.#n === 1 && (this.#o = et.subscribe(async (t) => {
      t && (await this.resumePausedMutations(), this.#e.onFocus());
    }), this.#a = H.subscribe(async (t) => {
      t && (await this.resumePausedMutations(), this.#e.onOnline());
    }));
  }
  unmount() {
    this.#n--, this.#n === 0 && (this.#o?.(), this.#o = void 0, this.#a?.(), this.#a = void 0);
  }
  isFetching(t) {
    return this.#e.findAll({ ...t, fetchStatus: "fetching" }).length;
  }
  isMutating(t) {
    return this.#t.findAll({ ...t, status: "pending" }).length;
  }
  /**
   * Imperative (non-reactive) way to retrieve data for a QueryKey.
   * Should only be used in callbacks or functions where reading the latest data is necessary, e.g. for optimistic updates.
   *
   * Hint: Do not use this function inside a component, because it won't receive updates.
   * Use `useQuery` to create a `QueryObserver` that subscribes to changes.
   */
  getQueryData(t) {
    const e = this.defaultQueryOptions({ queryKey: t });
    return this.#e.get(e.queryHash)?.state.data;
  }
  ensureQueryData(t) {
    const e = this.defaultQueryOptions(t), s = this.#e.build(this, e), r = s.state.data;
    return r === void 0 ? this.fetchQuery(t) : (t.revalidateIfStale && s.isStaleByTime(F(e.staleTime, s)) && this.prefetchQuery(e), Promise.resolve(r));
  }
  getQueriesData(t) {
    return this.#e.findAll(t).map(({ queryKey: e, state: s }) => {
      const r = s.data;
      return [e, r];
    });
  }
  setQueryData(t, e, s) {
    const r = this.defaultQueryOptions({ queryKey: t }), n = this.#e.get(
      r.queryHash
    )?.state.data, a = Wt(e, n);
    if (a !== void 0)
      return this.#e.build(this, r).setData(a, { ...s, manual: !0 });
  }
  setQueriesData(t, e, s) {
    return b.batch(
      () => this.#e.findAll(t).map(({ queryKey: r }) => [
        r,
        this.setQueryData(r, e, s)
      ])
    );
  }
  getQueryState(t) {
    const e = this.defaultQueryOptions({ queryKey: t });
    return this.#e.get(
      e.queryHash
    )?.state;
  }
  removeQueries(t) {
    const e = this.#e;
    b.batch(() => {
      e.findAll(t).forEach((s) => {
        e.remove(s);
      });
    });
  }
  resetQueries(t, e) {
    const s = this.#e;
    return b.batch(() => (s.findAll(t).forEach((r) => {
      r.reset();
    }), this.refetchQueries(
      {
        type: "active",
        ...t
      },
      e
    )));
  }
  cancelQueries(t, e = {}) {
    const s = { revert: !0, ...e }, r = b.batch(
      () => this.#e.findAll(t).map((i) => i.cancel(s))
    );
    return Promise.all(r).then(S).catch(S);
  }
  invalidateQueries(t, e = {}) {
    return b.batch(() => (this.#e.findAll(t).forEach((s) => {
      s.invalidate();
    }), t?.refetchType === "none" ? Promise.resolve() : this.refetchQueries(
      {
        ...t,
        type: t?.refetchType ?? t?.type ?? "active"
      },
      e
    )));
  }
  refetchQueries(t, e = {}) {
    const s = {
      ...e,
      cancelRefetch: e.cancelRefetch ?? !0
    }, r = b.batch(
      () => this.#e.findAll(t).filter((i) => !i.isDisabled() && !i.isStatic()).map((i) => {
        let n = i.fetch(void 0, s);
        return s.throwOnError || (n = n.catch(S)), i.state.fetchStatus === "paused" ? Promise.resolve() : n;
      })
    );
    return Promise.all(r).then(S);
  }
  fetchQuery(t) {
    const e = this.defaultQueryOptions(t);
    e.retry === void 0 && (e.retry = !1);
    const s = this.#e.build(this, e);
    return s.isStaleByTime(
      F(e.staleTime, s)
    ) ? s.fetch(e) : Promise.resolve(s.state.data);
  }
  prefetchQuery(t) {
    return this.fetchQuery(t).then(S).catch(S);
  }
  fetchInfiniteQuery(t) {
    return t._type = "infinite", this.fetchQuery(t);
  }
  prefetchInfiniteQuery(t) {
    return this.fetchInfiniteQuery(t).then(S).catch(S);
  }
  ensureInfiniteQueryData(t) {
    return t._type = "infinite", this.ensureQueryData(t);
  }
  resumePausedMutations() {
    return H.isOnline() ? this.#t.resumePausedMutations() : Promise.resolve();
  }
  getQueryCache() {
    return this.#e;
  }
  getMutationCache() {
    return this.#t;
  }
  getDefaultOptions() {
    return this.#s;
  }
  setDefaultOptions(t) {
    this.#s = t;
  }
  setQueryDefaults(t, e) {
    this.#r.set(T(t), {
      queryKey: t,
      defaultOptions: e
    });
  }
  getQueryDefaults(t) {
    const e = [...this.#r.values()], s = {};
    return e.forEach((r) => {
      A(t, r.queryKey) && Object.assign(s, r.defaultOptions);
    }), s;
  }
  setMutationDefaults(t, e) {
    this.#i.set(T(t), {
      mutationKey: t,
      defaultOptions: e
    });
  }
  getMutationDefaults(t) {
    const e = [...this.#i.values()], s = {};
    return e.forEach((r) => {
      A(t, r.mutationKey) && Object.assign(s, r.defaultOptions);
    }), s;
  }
  defaultQueryOptions(t) {
    if (t._defaulted)
      return t;
    const e = {
      ...this.#s.queries,
      ...this.getQueryDefaults(t.queryKey),
      ...t,
      _defaulted: !0
    };
    return e.queryHash || (e.queryHash = st(
      e.queryKey,
      e
    )), e.refetchOnReconnect === void 0 && (e.refetchOnReconnect = e.networkMode !== "always"), e.throwOnError === void 0 && (e.throwOnError = !!e.suspense), !e.networkMode && e.persister && (e.networkMode = "offlineFirst"), e.queryFn === rt && (e.enabled = !1), e;
  }
  defaultMutationOptions(t) {
    return t?._defaulted ? t : {
      ...this.#s.mutations,
      ...t?.mutationKey && this.getMutationDefaults(t.mutationKey),
      ...t,
      _defaulted: !0
    };
  }
  clear() {
    this.#e.clear(), this.#t.clear();
  }
};
function Me({
  streamFn: t,
  refetchMode: e = "reset",
  reducer: s = (i, n) => St(i, n),
  initialValue: r = []
}) {
  return async (i) => {
    const n = i.client.getQueryCache().find({ queryKey: i.queryKey, exact: !0 }), a = !!n && n.isFetched();
    a && e === "reset" && n.setState({
      ...n.resetState,
      fetchStatus: "fetching"
    });
    let h = r, o = !1;
    const d = wt(
      {
        client: i.client,
        meta: i.meta,
        queryKey: i.queryKey,
        pageParam: i.pageParam,
        direction: i.direction
      },
      () => i.signal,
      () => o = !0
    ), u = await t(d), l = a && e === "replace";
    for await (const c of u) {
      if (o)
        break;
      l ? h = s(h, c) : i.client.setQueryData(
        i.queryKey,
        (y) => s(y === void 0 ? r : y, c)
      );
    }
    return l && !o && i.client.setQueryData(i.queryKey, h), i.client.getQueryData(i.queryKey) ?? r;
  };
}
var Fe = /* @__PURE__ */ Symbol("dataTagSymbol"), De = /* @__PURE__ */ Symbol("dataTagErrorSymbol"), Te = /* @__PURE__ */ Symbol("unsetMarker"), It = p.createContext(
  void 0
), M = (t) => {
  const e = p.useContext(It);
  if (t)
    return t;
  if (!e)
    throw new Error("No QueryClient set, use QueryClientProvider to set one");
  return e;
}, Ie = ({
  client: t,
  children: e
}) => (p.useEffect(() => (t.mount(), () => {
  t.unmount();
}), [t]), /* @__PURE__ */ bt.jsx(It.Provider, { value: t, children: e })), xt = p.createContext(!1), At = () => p.useContext(xt), xe = xt.Provider;
function qt() {
  let t = !1;
  return {
    clearReset: () => {
      t = !1;
    },
    reset: () => {
      t = !0;
    },
    isReset: () => t
  };
}
var Ut = p.createContext(qt()), jt = () => p.useContext(Ut), Ae = ({
  children: t
}) => {
  const [e] = p.useState(() => qt());
  return /* @__PURE__ */ bt.jsx(Ut.Provider, { value: e, children: typeof t == "function" ? t(e) : t });
}, kt = (t, e, s) => {
  const r = s?.state.error && typeof t.throwOnError == "function" ? it(t.throwOnError, [s.state.error, s]) : t.throwOnError;
  (t.suspense || t.experimental_prefetchInRender || r) && (e.isReset() || (t.retryOnMount = !1));
}, Kt = (t) => {
  p.useEffect(() => {
    t.clearReset();
  }, [t]);
}, _t = ({
  result: t,
  errorResetBoundary: e,
  throwOnError: s,
  query: r,
  suspense: i
}) => t.isError && !e.isReset() && !t.isFetching && r && (i && t.data === void 0 || it(s, [t.error, r])), at = (t, e) => e.state.data === void 0, Lt = (t) => {
  if (t.suspense) {
    const s = (i) => i === "static" ? i : Math.max(i ?? 1e3, 1e3), r = t.staleTime;
    t.staleTime = typeof r == "function" ? (...i) => s(r(...i)) : s(r), typeof t.gcTime == "number" && (t.gcTime = Math.max(
      t.gcTime,
      1e3
    ));
  }
}, be = (t, e) => t.isLoading && t.isFetching && !e, Y = (t, e) => t?.suspense && e.isPending, tt = (t, e, s) => e.fetchOptimistic(t).catch(() => {
  s.clearReset();
});
function Oe({
  queries: t,
  ...e
}, s) {
  const r = M(s), i = At(), n = jt(), a = p.useMemo(
    () => t.map((f) => {
      const v = r.defaultQueryOptions(
        f
      );
      return v._optimisticResults = i ? "isRestoring" : "optimistic", v;
    }),
    [t, r, i]
  );
  a.forEach((f) => {
    Lt(f);
    const v = r.getQueryCache().get(f.queryHash);
    kt(f, n, v);
  }), Kt(n);
  const [h] = p.useState(
    () => new me(
      r,
      a,
      e
    )
  ), [o, d, u] = h.getOptimisticResult(
    a,
    e.combine
  ), l = !i && e.subscribed !== !1;
  p.useSyncExternalStore(
    p.useCallback(
      (f) => l ? h.subscribe(b.batchCalls(f)) : S,
      [h, l]
    ),
    () => h.getCurrentResult(),
    () => h.getCurrentResult()
  ), p.useEffect(() => {
    h.setQueries(
      a,
      e
    );
  }, [a, e, h]);
  const y = o.some(
    (f, v) => Y(a[v], f)
  ) ? o.flatMap((f, v) => {
    const m = a[v];
    if (m && Y(m, f)) {
      const O = new j(r, m);
      return tt(m, O, n);
    }
    return [];
  }) : [];
  if (y.length > 0)
    throw Promise.all(y);
  const g = o.find(
    (f, v) => {
      const m = a[v];
      return m && _t({
        result: f,
        errorResetBoundary: n,
        throwOnError: m.throwOnError,
        query: r.getQueryCache().get(m.queryHash),
        suspense: m.suspense
      });
    }
  );
  if (g?.error)
    throw g.error;
  return d(u());
}
function G(t, e, s) {
  const r = At(), i = jt(), n = M(s), a = n.defaultQueryOptions(t);
  n.getDefaultOptions().queries?._experimental_beforeQuery?.(
    a
  );
  const h = n.getQueryCache().get(a.queryHash), o = t.subscribed !== !1;
  a._optimisticResults = r ? "isRestoring" : o ? "optimistic" : void 0, Lt(a), kt(a, i, h), Kt(i);
  const d = !n.getQueryCache().get(a.queryHash), [u] = p.useState(
    () => new e(
      n,
      a
    )
  ), l = u.getOptimisticResult(a), c = !r && o;
  if (p.useSyncExternalStore(
    p.useCallback(
      (y) => {
        const g = c ? u.subscribe(b.batchCalls(y)) : S;
        return u.updateResult(), g;
      },
      [u, c]
    ),
    () => u.getCurrentResult(),
    () => u.getCurrentResult()
  ), p.useEffect(() => {
    u.setOptions(a);
  }, [a, u]), Y(a, l))
    throw tt(a, u, i);
  if (_t({
    result: l,
    errorResetBoundary: i,
    throwOnError: a.throwOnError,
    query: h,
    suspense: a.suspense
  }))
    throw l.error;
  return n.getDefaultOptions().queries?._experimental_afterQuery?.(
    a,
    l
  ), a.experimental_prefetchInRender && !U.isServer() && be(l, r) && (d ? (
    // Fetch immediately on render in order to ensure `.promise` is resolved even if the component is unmounted
    tt(a, u, i)
  ) : (
    // subscribe to the "cache promise" so that we can finalize the currentThenable once data comes in
    h?.promise
  ))?.catch(S).finally(() => {
    u.updateResult();
  }), a.notifyOnChangeProps ? l : u.trackResult(l);
}
function qe(t, e) {
  return G(t, j, e);
}
function Ue(t, e) {
  return G(
    {
      ...t,
      enabled: !0,
      suspense: !0,
      throwOnError: at,
      placeholderData: void 0
    },
    j,
    e
  );
}
function je(t, e) {
  return G(
    {
      ...t,
      enabled: !0,
      suspense: !0,
      throwOnError: at
    },
    Dt,
    e
  );
}
function ke(t, e) {
  return Oe(
    {
      ...t,
      queries: t.queries.map((s) => ({
        ...s,
        suspense: !0,
        throwOnError: at,
        enabled: !0,
        placeholderData: void 0
      }))
    },
    e
  );
}
function Ke(t, e) {
  const s = M(e);
  s.getQueryState(t.queryKey) || s.prefetchQuery(t);
}
function _e(t, e) {
  const s = M(e);
  s.getQueryState(t.queryKey) || s.prefetchInfiniteQuery(t);
}
function Le(t) {
  return t;
}
function Ne(t) {
  return t;
}
var He = ({
  children: t,
  options: e = {},
  state: s,
  queryClient: r
}) => {
  const i = M(r), n = p.useRef(e);
  p.useEffect(() => {
    n.current = e;
  });
  const a = p.useMemo(() => {
    if (s) {
      if (typeof s != "object")
        return;
      const h = i.getQueryCache(), o = s.queries || [], d = [], u = [];
      for (const l of o) {
        const c = h.get(l.queryHash);
        c ? (l.state.dataUpdatedAt > c.state.dataUpdatedAt || l.promise && c.state.status !== "pending" && c.state.fetchStatus !== "fetching" && l.dehydratedAt !== void 0 && l.dehydratedAt > c.state.dataUpdatedAt) && u.push(l) : d.push(l);
      }
      if (d.length > 0 && ft(i, { queries: d }, n.current), u.length > 0)
        return u;
    }
  }, [i, s]);
  return p.useEffect(() => {
    a && ft(i, { queries: a }, n.current);
  }, [i, a]), t;
};
function Be(t, e) {
  const s = M(e), r = s.getQueryCache();
  return p.useSyncExternalStore(
    p.useCallback(
      (i) => r.subscribe(b.batchCalls(i)),
      [r]
    ),
    () => s.isFetching(t),
    () => s.isFetching(t)
  );
}
function ze(t, e) {
  const s = M(e);
  return Se(
    { filters: { ...t, status: "pending" } },
    s
  ).length;
}
function gt(t, e) {
  return t.findAll(e.filters).map(
    (s) => e.select ? e.select(s) : s.state
  );
}
function Se(t = {}, e) {
  const s = M(e).getMutationCache(), r = p.useRef(t), i = p.useRef(null);
  return i.current === null && (i.current = gt(s, t)), p.useEffect(() => {
    r.current = t;
  }), p.useSyncExternalStore(
    p.useCallback(
      (n) => s.subscribe(() => {
        const a = z(
          i.current,
          gt(s, r.current)
        );
        i.current !== a && (i.current = a, b.schedule(n));
      }),
      [s]
    ),
    () => i.current,
    () => i.current
  );
}
function Ge(t, e) {
  const s = M(e), [r] = p.useState(
    () => new ye(
      s,
      t
    )
  );
  p.useEffect(() => {
    r.setOptions(t);
  }, [r, t]);
  const i = p.useSyncExternalStore(
    p.useCallback(
      (a) => r.subscribe(b.batchCalls(a)),
      [r]
    ),
    () => r.getCurrentResult(),
    () => r.getCurrentResult()
  ), n = p.useCallback(
    (a, h) => {
      r.mutate(a, h).catch(S);
    },
    [r]
  );
  if (i.error && it(r.options.throwOnError, [i.error]))
    throw i.error;
  return { ...i, mutate: n, mutateAsync: i.mutate };
}
function We(t) {
  return t;
}
function $e(t, e) {
  return G(
    t,
    Dt,
    e
  );
}
export {
  B as CancelledError,
  He as HydrationBoundary,
  Dt as InfiniteQueryObserver,
  xe as IsRestoringProvider,
  de as Mutation,
  pe as MutationCache,
  ye as MutationObserver,
  me as QueriesObserver,
  ce as Query,
  ge as QueryCache,
  Ee as QueryClient,
  It as QueryClientContext,
  Ie as QueryClientProvider,
  Ae as QueryErrorResetBoundary,
  j as QueryObserver,
  De as dataTagErrorSymbol,
  Fe as dataTagSymbol,
  re as defaultScheduler,
  te as defaultShouldDehydrateMutation,
  ee as defaultShouldDehydrateQuery,
  Ce as dehydrate,
  U as environmentManager,
  Me as experimental_streamedQuery,
  et as focusManager,
  T as hashKey,
  ft as hydrate,
  Ne as infiniteQueryOptions,
  Qe as isCancelledError,
  Gt as isServer,
  Pe as keepPreviousData,
  ht as matchMutation,
  ut as matchQuery,
  We as mutationOptions,
  S as noop,
  b as notifyManager,
  H as onlineManager,
  A as partialMatchKey,
  Le as queryOptions,
  z as replaceEqualDeep,
  it as shouldThrowError,
  rt as skipToken,
  D as timeoutManager,
  Te as unsetMarker,
  $e as useInfiniteQuery,
  Be as useIsFetching,
  ze as useIsMutating,
  At as useIsRestoring,
  Ge as useMutation,
  Se as useMutationState,
  _e as usePrefetchInfiniteQuery,
  Ke as usePrefetchQuery,
  Oe as useQueries,
  qe as useQuery,
  M as useQueryClient,
  jt as useQueryErrorResetBoundary,
  je as useSuspenseInfiniteQuery,
  ke as useSuspenseQueries,
  Ue as useSuspenseQuery
};
