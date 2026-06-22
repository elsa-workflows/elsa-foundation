import { useState as I, useRef as me, useMemo as oe, useCallback as se, useEffect as V } from "react";
var re = { exports: {} }, U = {};
var be;
function ze() {
  if (be) return U;
  be = 1;
  var n = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function t(o, s, r) {
    var c = null;
    if (r !== void 0 && (c = "" + r), s.key !== void 0 && (c = "" + s.key), "key" in s) {
      r = {};
      for (var a in s)
        a !== "key" && (r[a] = s[a]);
    } else r = s;
    return s = r.ref, {
      $$typeof: n,
      type: o,
      key: c,
      ref: s !== void 0 ? s : null,
      props: r
    };
  }
  return U.Fragment = e, U.jsx = t, U.jsxs = t, U;
}
var we;
function Je() {
  return we || (we = 1, re.exports = ze()), re.exports;
}
var f = Je();
class R extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.HttpError}.
   *
   * @param {string} errorMessage A descriptive error message.
   * @param {number} statusCode The HTTP status code represented by this error.
   */
  constructor(e, t) {
    const o = new.target.prototype;
    super(`${e}: Status code '${t}'`), this.statusCode = t, this.__proto__ = o;
  }
}
class le extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.TimeoutError}.
   *
   * @param {string} errorMessage A descriptive error message.
   */
  constructor(e = "A timeout occurred.") {
    const t = new.target.prototype;
    super(e), this.__proto__ = t;
  }
}
class T extends Error {
  /** Constructs a new instance of {@link AbortError}.
   *
   * @param {string} errorMessage A descriptive error message.
   */
  constructor(e = "An abort occurred.") {
    const t = new.target.prototype;
    super(e), this.__proto__ = t;
  }
}
class Xe extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.UnsupportedTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const o = new.target.prototype;
    super(e), this.transport = t, this.errorType = "UnsupportedTransportError", this.__proto__ = o;
  }
}
class Ve extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.DisabledTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const o = new.target.prototype;
    super(e), this.transport = t, this.errorType = "DisabledTransportError", this.__proto__ = o;
  }
}
class Ke extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.FailedToStartTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const o = new.target.prototype;
    super(e), this.transport = t, this.errorType = "FailedToStartTransportError", this.__proto__ = o;
  }
}
class Se extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.FailedToNegotiateWithServerError}.
   *
   * @param {string} message A descriptive error message.
   */
  constructor(e) {
    const t = new.target.prototype;
    super(e), this.errorType = "FailedToNegotiateWithServerError", this.__proto__ = t;
  }
}
class Ge extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.AggregateErrors}.
   *
   * @param {string} message A descriptive error message.
   * @param {Error[]} innerErrors The collection of errors this error is aggregating.
   */
  constructor(e, t) {
    const o = new.target.prototype;
    super(e), this.innerErrors = t, this.__proto__ = o;
  }
}
class Re {
  constructor(e, t, o) {
    this.statusCode = e, this.statusText = t, this.content = o;
  }
}
class Z {
  get(e, t) {
    return this.send({
      ...t,
      method: "GET",
      url: e
    });
  }
  post(e, t) {
    return this.send({
      ...t,
      method: "POST",
      url: e
    });
  }
  delete(e, t) {
    return this.send({
      ...t,
      method: "DELETE",
      url: e
    });
  }
  /** Gets all cookies that apply to the specified URL.
   *
   * @param url The URL that the cookies are valid for.
   * @returns {string} A string containing all the key-value cookie pairs for the specified URL.
   */
  // @ts-ignore
  getCookieString(e) {
    return "";
  }
}
var i;
(function(n) {
  n[n.Trace = 0] = "Trace", n[n.Debug = 1] = "Debug", n[n.Information = 2] = "Information", n[n.Warning = 3] = "Warning", n[n.Error = 4] = "Error", n[n.Critical = 5] = "Critical", n[n.None = 6] = "None";
})(i || (i = {}));
class O {
  constructor() {
  }
  /** @inheritDoc */
  // eslint-disable-next-line
  log(e, t) {
  }
}
O.instance = new O();
const Ye = "8.0.17";
class m {
  static isRequired(e, t) {
    if (e == null)
      throw new Error(`The '${t}' argument is required.`);
  }
  static isNotEmpty(e, t) {
    if (!e || e.match(/^\s*$/))
      throw new Error(`The '${t}' argument should not be empty.`);
  }
  static isIn(e, t, o) {
    if (!(e in t))
      throw new Error(`Unknown ${o} value: ${e}.`);
  }
}
class _ {
  // react-native has a window but no document so we should check both
  static get isBrowser() {
    return !_.isNode && typeof window == "object" && typeof window.document == "object";
  }
  // WebWorkers don't have a window object so the isBrowser check would fail
  static get isWebWorker() {
    return !_.isNode && typeof self == "object" && "importScripts" in self;
  }
  // react-native has a window but no document
  static get isReactNative() {
    return !_.isNode && typeof window == "object" && typeof window.document > "u";
  }
  // Node apps shouldn't have a window object, but WebWorkers don't either
  // so we need to check for both WebWorker and window
  static get isNode() {
    return typeof process < "u" && process.release && process.release.name === "node";
  }
}
function F(n, e) {
  let t = "";
  return D(n) ? (t = `Binary data of length ${n.byteLength}`, e && (t += `. Content: '${Ze(n)}'`)) : typeof n == "string" && (t = `String data of length ${n.length}`, e && (t += `. Content: '${n}'`)), t;
}
function Ze(n) {
  const e = new Uint8Array(n);
  let t = "";
  return e.forEach((o) => {
    const s = o < 16 ? "0" : "";
    t += `0x${s}${o.toString(16)} `;
  }), t.substr(0, t.length - 1);
}
function D(n) {
  return n && typeof ArrayBuffer < "u" && (n instanceof ArrayBuffer || // Sometimes we get an ArrayBuffer that doesn't satisfy instanceof
  n.constructor && n.constructor.name === "ArrayBuffer");
}
async function $e(n, e, t, o, s, r) {
  const c = {}, [a, l] = M();
  c[a] = l, n.log(i.Trace, `(${e} transport) sending data. ${F(s, r.logMessageContent)}.`);
  const u = D(s) ? "arraybuffer" : "text", g = await t.post(o, {
    content: s,
    headers: { ...c, ...r.headers },
    responseType: u,
    timeout: r.timeout,
    withCredentials: r.withCredentials
  });
  n.log(i.Trace, `(${e} transport) request complete. Response status: ${g.statusCode}.`);
}
function Qe(n) {
  return n === void 0 ? new Y(i.Information) : n === null ? O.instance : n.log !== void 0 ? n : new Y(n);
}
class et {
  constructor(e, t) {
    this._subject = e, this._observer = t;
  }
  dispose() {
    const e = this._subject.observers.indexOf(this._observer);
    e > -1 && this._subject.observers.splice(e, 1), this._subject.observers.length === 0 && this._subject.cancelCallback && this._subject.cancelCallback().catch((t) => {
    });
  }
}
class Y {
  constructor(e) {
    this._minLevel = e, this.out = console;
  }
  log(e, t) {
    if (e >= this._minLevel) {
      const o = `[${(/* @__PURE__ */ new Date()).toISOString()}] ${i[e]}: ${t}`;
      switch (e) {
        case i.Critical:
        case i.Error:
          this.out.error(o);
          break;
        case i.Warning:
          this.out.warn(o);
          break;
        case i.Information:
          this.out.info(o);
          break;
        default:
          this.out.log(o);
          break;
      }
    }
  }
}
function M() {
  let n = "X-SignalR-User-Agent";
  return _.isNode && (n = "User-Agent"), [n, tt(Ye, nt(), st(), ot())];
}
function tt(n, e, t, o) {
  let s = "Microsoft SignalR/";
  const r = n.split(".");
  return s += `${r[0]}.${r[1]}`, s += ` (${n}; `, e && e !== "" ? s += `${e}; ` : s += "Unknown OS; ", s += `${t}`, o ? s += `; ${o}` : s += "; Unknown Runtime Version", s += ")", s;
}
function nt() {
  if (_.isNode)
    switch (process.platform) {
      case "win32":
        return "Windows NT";
      case "darwin":
        return "macOS";
      case "linux":
        return "Linux";
      default:
        return process.platform;
    }
  else
    return "";
}
function ot() {
  if (_.isNode)
    return process.versions.node;
}
function st() {
  return _.isNode ? "NodeJS" : "Browser";
}
function ie(n) {
  return n.stack ? n.stack : n.message ? n.message : `${n}`;
}
function rt() {
  if (typeof globalThis < "u")
    return globalThis;
  if (typeof self < "u")
    return self;
  if (typeof window < "u")
    return window;
  if (typeof global < "u")
    return global;
  throw new Error("could not find global");
}
class it extends Z {
  constructor(e) {
    if (super(), this._logger = e, typeof fetch > "u" || _.isNode) {
      const t = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      this._jar = new (t("tough-cookie")).CookieJar(), typeof fetch > "u" ? this._fetchType = t("node-fetch") : this._fetchType = fetch, this._fetchType = t("fetch-cookie")(this._fetchType, this._jar);
    } else
      this._fetchType = fetch.bind(rt());
    if (typeof AbortController > "u") {
      const t = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      this._abortControllerType = t("abort-controller");
    } else
      this._abortControllerType = AbortController;
  }
  /** @inheritDoc */
  async send(e) {
    if (e.abortSignal && e.abortSignal.aborted)
      throw new T();
    if (!e.method)
      throw new Error("No method defined.");
    if (!e.url)
      throw new Error("No url defined.");
    const t = new this._abortControllerType();
    let o;
    e.abortSignal && (e.abortSignal.onabort = () => {
      t.abort(), o = new T();
    });
    let s = null;
    if (e.timeout) {
      const l = e.timeout;
      s = setTimeout(() => {
        t.abort(), this._logger.log(i.Warning, "Timeout from HTTP request."), o = new le();
      }, l);
    }
    e.content === "" && (e.content = void 0), e.content && (e.headers = e.headers || {}, D(e.content) ? e.headers["Content-Type"] = "application/octet-stream" : e.headers["Content-Type"] = "text/plain;charset=UTF-8");
    let r;
    try {
      r = await this._fetchType(e.url, {
        body: e.content,
        cache: "no-cache",
        credentials: e.withCredentials === !0 ? "include" : "same-origin",
        headers: {
          "X-Requested-With": "XMLHttpRequest",
          ...e.headers
        },
        method: e.method,
        mode: "cors",
        redirect: "follow",
        signal: t.signal
      });
    } catch (l) {
      throw o || (this._logger.log(i.Warning, `Error from HTTP request. ${l}.`), l);
    } finally {
      s && clearTimeout(s), e.abortSignal && (e.abortSignal.onabort = null);
    }
    if (!r.ok) {
      const l = await Ce(r, "text");
      throw new R(l || r.statusText, r.status);
    }
    const a = await Ce(r, e.responseType);
    return new Re(r.status, r.statusText, a);
  }
  getCookieString(e) {
    let t = "";
    return _.isNode && this._jar && this._jar.getCookies(e, (o, s) => t = s.join("; ")), t;
  }
}
function Ce(n, e) {
  let t;
  switch (e) {
    case "arraybuffer":
      t = n.arrayBuffer();
      break;
    case "text":
      t = n.text();
      break;
    case "blob":
    case "document":
    case "json":
      throw new Error(`${e} is not supported.`);
    default:
      t = n.text();
      break;
  }
  return t;
}
class ct extends Z {
  constructor(e) {
    super(), this._logger = e;
  }
  /** @inheritDoc */
  send(e) {
    return e.abortSignal && e.abortSignal.aborted ? Promise.reject(new T()) : e.method ? e.url ? new Promise((t, o) => {
      const s = new XMLHttpRequest();
      s.open(e.method, e.url, !0), s.withCredentials = e.withCredentials === void 0 ? !0 : e.withCredentials, s.setRequestHeader("X-Requested-With", "XMLHttpRequest"), e.content === "" && (e.content = void 0), e.content && (D(e.content) ? s.setRequestHeader("Content-Type", "application/octet-stream") : s.setRequestHeader("Content-Type", "text/plain;charset=UTF-8"));
      const r = e.headers;
      r && Object.keys(r).forEach((c) => {
        s.setRequestHeader(c, r[c]);
      }), e.responseType && (s.responseType = e.responseType), e.abortSignal && (e.abortSignal.onabort = () => {
        s.abort(), o(new T());
      }), e.timeout && (s.timeout = e.timeout), s.onload = () => {
        e.abortSignal && (e.abortSignal.onabort = null), s.status >= 200 && s.status < 300 ? t(new Re(s.status, s.statusText, s.response || s.responseText)) : o(new R(s.response || s.responseText || s.statusText, s.status));
      }, s.onerror = () => {
        this._logger.log(i.Warning, `Error from HTTP request. ${s.status}: ${s.statusText}.`), o(new R(s.statusText, s.status));
      }, s.ontimeout = () => {
        this._logger.log(i.Warning, "Timeout from HTTP request."), o(new le());
      }, s.send(e.content);
    }) : Promise.reject(new Error("No url defined.")) : Promise.reject(new Error("No method defined."));
  }
}
class at extends Z {
  /** Creates a new instance of the {@link @microsoft/signalr.DefaultHttpClient}, using the provided {@link @microsoft/signalr.ILogger} to log messages. */
  constructor(e) {
    if (super(), typeof fetch < "u" || _.isNode)
      this._httpClient = new it(e);
    else if (typeof XMLHttpRequest < "u")
      this._httpClient = new ct(e);
    else
      throw new Error("No usable HttpClient found.");
  }
  /** @inheritDoc */
  send(e) {
    return e.abortSignal && e.abortSignal.aborted ? Promise.reject(new T()) : e.method ? e.url ? this._httpClient.send(e) : Promise.reject(new Error("No url defined.")) : Promise.reject(new Error("No method defined."));
  }
  getCookieString(e) {
    return this._httpClient.getCookieString(e);
  }
}
class y {
  static write(e) {
    return `${e}${y.RecordSeparator}`;
  }
  static parse(e) {
    if (e[e.length - 1] !== y.RecordSeparator)
      throw new Error("Message is incomplete.");
    const t = e.split(y.RecordSeparator);
    return t.pop(), t;
  }
}
y.RecordSeparatorCode = 30;
y.RecordSeparator = String.fromCharCode(y.RecordSeparatorCode);
class lt {
  // Handshake request is always JSON
  writeHandshakeRequest(e) {
    return y.write(JSON.stringify(e));
  }
  parseHandshakeResponse(e) {
    let t, o;
    if (D(e)) {
      const a = new Uint8Array(e), l = a.indexOf(y.RecordSeparatorCode);
      if (l === -1)
        throw new Error("Message is incomplete.");
      const u = l + 1;
      t = String.fromCharCode.apply(null, Array.prototype.slice.call(a.slice(0, u))), o = a.byteLength > u ? a.slice(u).buffer : null;
    } else {
      const a = e, l = a.indexOf(y.RecordSeparator);
      if (l === -1)
        throw new Error("Message is incomplete.");
      const u = l + 1;
      t = a.substring(0, u), o = a.length > u ? a.substring(u) : null;
    }
    const s = y.parse(t), r = JSON.parse(s[0]);
    if (r.type)
      throw new Error("Expected a handshake response from the server.");
    return [o, r];
  }
}
var d;
(function(n) {
  n[n.Invocation = 1] = "Invocation", n[n.StreamItem = 2] = "StreamItem", n[n.Completion = 3] = "Completion", n[n.StreamInvocation = 4] = "StreamInvocation", n[n.CancelInvocation = 5] = "CancelInvocation", n[n.Ping = 6] = "Ping", n[n.Close = 7] = "Close", n[n.Ack = 8] = "Ack", n[n.Sequence = 9] = "Sequence";
})(d || (d = {}));
class ht {
  constructor() {
    this.observers = [];
  }
  next(e) {
    for (const t of this.observers)
      t.next(e);
  }
  error(e) {
    for (const t of this.observers)
      t.error && t.error(e);
  }
  complete() {
    for (const e of this.observers)
      e.complete && e.complete();
  }
  subscribe(e) {
    return this.observers.push(e), new et(this, e);
  }
}
class ut {
  constructor(e, t, o) {
    this._bufferSize = 1e5, this._messages = [], this._totalMessageCount = 0, this._waitForSequenceMessage = !1, this._nextReceivingSequenceId = 1, this._latestReceivedSequenceId = 0, this._bufferedByteCount = 0, this._reconnectInProgress = !1, this._protocol = e, this._connection = t, this._bufferSize = o;
  }
  async _send(e) {
    const t = this._protocol.writeMessage(e);
    let o = Promise.resolve();
    if (this._isInvocationMessage(e)) {
      this._totalMessageCount++;
      let s = () => {
      }, r = () => {
      };
      D(t) ? this._bufferedByteCount += t.byteLength : this._bufferedByteCount += t.length, this._bufferedByteCount >= this._bufferSize && (o = new Promise((c, a) => {
        s = c, r = a;
      })), this._messages.push(new dt(t, this._totalMessageCount, s, r));
    }
    try {
      this._reconnectInProgress || await this._connection.send(t);
    } catch {
      this._disconnected();
    }
    await o;
  }
  _ack(e) {
    let t = -1;
    for (let o = 0; o < this._messages.length; o++) {
      const s = this._messages[o];
      if (s._id <= e.sequenceId)
        t = o, D(s._message) ? this._bufferedByteCount -= s._message.byteLength : this._bufferedByteCount -= s._message.length, s._resolver();
      else if (this._bufferedByteCount < this._bufferSize)
        s._resolver();
      else
        break;
    }
    t !== -1 && (this._messages = this._messages.slice(t + 1));
  }
  _shouldProcessMessage(e) {
    if (this._waitForSequenceMessage)
      return e.type !== d.Sequence ? !1 : (this._waitForSequenceMessage = !1, !0);
    if (!this._isInvocationMessage(e))
      return !0;
    const t = this._nextReceivingSequenceId;
    return this._nextReceivingSequenceId++, t <= this._latestReceivedSequenceId ? (t === this._latestReceivedSequenceId && this._ackTimer(), !1) : (this._latestReceivedSequenceId = t, this._ackTimer(), !0);
  }
  _resetSequence(e) {
    if (e.sequenceId > this._nextReceivingSequenceId) {
      this._connection.stop(new Error("Sequence ID greater than amount of messages we've received."));
      return;
    }
    this._nextReceivingSequenceId = e.sequenceId;
  }
  _disconnected() {
    this._reconnectInProgress = !0, this._waitForSequenceMessage = !0;
  }
  async _resend() {
    const e = this._messages.length !== 0 ? this._messages[0]._id : this._totalMessageCount + 1;
    await this._connection.send(this._protocol.writeMessage({ type: d.Sequence, sequenceId: e }));
    const t = this._messages;
    for (const o of t)
      await this._connection.send(o._message);
    this._reconnectInProgress = !1;
  }
  _dispose(e) {
    e ?? (e = new Error("Unable to reconnect to server."));
    for (const t of this._messages)
      t._rejector(e);
  }
  _isInvocationMessage(e) {
    switch (e.type) {
      case d.Invocation:
      case d.StreamItem:
      case d.Completion:
      case d.StreamInvocation:
      case d.CancelInvocation:
        return !0;
      case d.Close:
      case d.Sequence:
      case d.Ping:
      case d.Ack:
        return !1;
    }
  }
  _ackTimer() {
    this._ackTimerHandle === void 0 && (this._ackTimerHandle = setTimeout(async () => {
      try {
        this._reconnectInProgress || await this._connection.send(this._protocol.writeMessage({ type: d.Ack, sequenceId: this._latestReceivedSequenceId }));
      } catch {
      }
      clearTimeout(this._ackTimerHandle), this._ackTimerHandle = void 0;
    }, 1e3));
  }
}
class dt {
  constructor(e, t, o, s) {
    this._message = e, this._id = t, this._resolver = o, this._rejector = s;
  }
}
const gt = 30 * 1e3, ft = 15 * 1e3, pt = 1e5;
var p;
(function(n) {
  n.Disconnected = "Disconnected", n.Connecting = "Connecting", n.Connected = "Connected", n.Disconnecting = "Disconnecting", n.Reconnecting = "Reconnecting";
})(p || (p = {}));
class he {
  /** @internal */
  // Using a public static factory method means we can have a private constructor and an _internal_
  // create method that can be used by HubConnectionBuilder. An "internal" constructor would just
  // be stripped away and the '.d.ts' file would have no constructor, which is interpreted as a
  // public parameter-less constructor.
  static create(e, t, o, s, r, c, a) {
    return new he(e, t, o, s, r, c, a);
  }
  constructor(e, t, o, s, r, c, a) {
    this._nextKeepAlive = 0, this._freezeEventListener = () => {
      this._logger.log(i.Warning, "The page is being frozen, this will likely lead to the connection being closed and messages being lost. For more information see the docs at https://learn.microsoft.com/aspnet/core/signalr/javascript-client#bsleep");
    }, m.isRequired(e, "connection"), m.isRequired(t, "logger"), m.isRequired(o, "protocol"), this.serverTimeoutInMilliseconds = r ?? gt, this.keepAliveIntervalInMilliseconds = c ?? ft, this._statefulReconnectBufferSize = a ?? pt, this._logger = t, this._protocol = o, this.connection = e, this._reconnectPolicy = s, this._handshakeProtocol = new lt(), this.connection.onreceive = (l) => this._processIncomingData(l), this.connection.onclose = (l) => this._connectionClosed(l), this._callbacks = {}, this._methods = {}, this._closedCallbacks = [], this._reconnectingCallbacks = [], this._reconnectedCallbacks = [], this._invocationId = 0, this._receivedHandshakeResponse = !1, this._connectionState = p.Disconnected, this._connectionStarted = !1, this._cachedPingMessage = this._protocol.writeMessage({ type: d.Ping });
  }
  /** Indicates the state of the {@link HubConnection} to the server. */
  get state() {
    return this._connectionState;
  }
  /** Represents the connection id of the {@link HubConnection} on the server. The connection id will be null when the connection is either
   *  in the disconnected state or if the negotiation step was skipped.
   */
  get connectionId() {
    return this.connection && this.connection.connectionId || null;
  }
  /** Indicates the url of the {@link HubConnection} to the server. */
  get baseUrl() {
    return this.connection.baseUrl || "";
  }
  /**
   * Sets a new url for the HubConnection. Note that the url can only be changed when the connection is in either the Disconnected or
   * Reconnecting states.
   * @param {string} url The url to connect to.
   */
  set baseUrl(e) {
    if (this._connectionState !== p.Disconnected && this._connectionState !== p.Reconnecting)
      throw new Error("The HubConnection must be in the Disconnected or Reconnecting state to change the url.");
    if (!e)
      throw new Error("The HubConnection url must be a valid url.");
    this.connection.baseUrl = e;
  }
  /** Starts the connection.
   *
   * @returns {Promise<void>} A Promise that resolves when the connection has been successfully established, or rejects with an error.
   */
  start() {
    return this._startPromise = this._startWithStateTransitions(), this._startPromise;
  }
  async _startWithStateTransitions() {
    if (this._connectionState !== p.Disconnected)
      return Promise.reject(new Error("Cannot start a HubConnection that is not in the 'Disconnected' state."));
    this._connectionState = p.Connecting, this._logger.log(i.Debug, "Starting HubConnection.");
    try {
      await this._startInternal(), _.isBrowser && window.document.addEventListener("freeze", this._freezeEventListener), this._connectionState = p.Connected, this._connectionStarted = !0, this._logger.log(i.Debug, "HubConnection connected successfully.");
    } catch (e) {
      return this._connectionState = p.Disconnected, this._logger.log(i.Debug, `HubConnection failed to start successfully because of error '${e}'.`), Promise.reject(e);
    }
  }
  async _startInternal() {
    this._stopDuringStartError = void 0, this._receivedHandshakeResponse = !1;
    const e = new Promise((t, o) => {
      this._handshakeResolver = t, this._handshakeRejecter = o;
    });
    await this.connection.start(this._protocol.transferFormat);
    try {
      let t = this._protocol.version;
      this.connection.features.reconnect || (t = 1);
      const o = {
        protocol: this._protocol.name,
        version: t
      };
      if (this._logger.log(i.Debug, "Sending handshake request."), await this._sendMessage(this._handshakeProtocol.writeHandshakeRequest(o)), this._logger.log(i.Information, `Using HubProtocol '${this._protocol.name}'.`), this._cleanupTimeout(), this._resetTimeoutPeriod(), this._resetKeepAliveInterval(), await e, this._stopDuringStartError)
        throw this._stopDuringStartError;
      this.connection.features.reconnect && (this._messageBuffer = new ut(this._protocol, this.connection, this._statefulReconnectBufferSize), this.connection.features.disconnected = this._messageBuffer._disconnected.bind(this._messageBuffer), this.connection.features.resend = () => {
        if (this._messageBuffer)
          return this._messageBuffer._resend();
      }), this.connection.features.inherentKeepAlive || await this._sendMessage(this._cachedPingMessage);
    } catch (t) {
      throw this._logger.log(i.Debug, `Hub handshake failed with error '${t}' during start(). Stopping HubConnection.`), this._cleanupTimeout(), this._cleanupPingTimer(), await this.connection.stop(t), t;
    }
  }
  /** Stops the connection.
   *
   * @returns {Promise<void>} A Promise that resolves when the connection has been successfully terminated, or rejects with an error.
   */
  async stop() {
    const e = this._startPromise;
    this.connection.features.reconnect = !1, this._stopPromise = this._stopInternal(), await this._stopPromise;
    try {
      await e;
    } catch {
    }
  }
  _stopInternal(e) {
    if (this._connectionState === p.Disconnected)
      return this._logger.log(i.Debug, `Call to HubConnection.stop(${e}) ignored because it is already in the disconnected state.`), Promise.resolve();
    if (this._connectionState === p.Disconnecting)
      return this._logger.log(i.Debug, `Call to HttpConnection.stop(${e}) ignored because the connection is already in the disconnecting state.`), this._stopPromise;
    const t = this._connectionState;
    return this._connectionState = p.Disconnecting, this._logger.log(i.Debug, "Stopping HubConnection."), this._reconnectDelayHandle ? (this._logger.log(i.Debug, "Connection stopped during reconnect delay. Done reconnecting."), clearTimeout(this._reconnectDelayHandle), this._reconnectDelayHandle = void 0, this._completeClose(), Promise.resolve()) : (t === p.Connected && this._sendCloseMessage(), this._cleanupTimeout(), this._cleanupPingTimer(), this._stopDuringStartError = e || new T("The connection was stopped before the hub handshake could complete."), this.connection.stop(e));
  }
  async _sendCloseMessage() {
    try {
      await this._sendWithProtocol(this._createCloseMessage());
    } catch {
    }
  }
  /** Invokes a streaming hub method on the server using the specified name and arguments.
   *
   * @typeparam T The type of the items returned by the server.
   * @param {string} methodName The name of the server method to invoke.
   * @param {any[]} args The arguments used to invoke the server method.
   * @returns {IStreamResult<T>} An object that yields results from the server as they are received.
   */
  stream(e, ...t) {
    const [o, s] = this._replaceStreamingParams(t), r = this._createStreamInvocation(e, t, s);
    let c;
    const a = new ht();
    return a.cancelCallback = () => {
      const l = this._createCancelInvocation(r.invocationId);
      return delete this._callbacks[r.invocationId], c.then(() => this._sendWithProtocol(l));
    }, this._callbacks[r.invocationId] = (l, u) => {
      if (u) {
        a.error(u);
        return;
      } else l && (l.type === d.Completion ? l.error ? a.error(new Error(l.error)) : a.complete() : a.next(l.item));
    }, c = this._sendWithProtocol(r).catch((l) => {
      a.error(l), delete this._callbacks[r.invocationId];
    }), this._launchStreams(o, c), a;
  }
  _sendMessage(e) {
    return this._resetKeepAliveInterval(), this.connection.send(e);
  }
  /**
   * Sends a js object to the server.
   * @param message The js object to serialize and send.
   */
  _sendWithProtocol(e) {
    return this._messageBuffer ? this._messageBuffer._send(e) : this._sendMessage(this._protocol.writeMessage(e));
  }
  /** Invokes a hub method on the server using the specified name and arguments. Does not wait for a response from the receiver.
   *
   * The Promise returned by this method resolves when the client has sent the invocation to the server. The server may still
   * be processing the invocation.
   *
   * @param {string} methodName The name of the server method to invoke.
   * @param {any[]} args The arguments used to invoke the server method.
   * @returns {Promise<void>} A Promise that resolves when the invocation has been successfully sent, or rejects with an error.
   */
  send(e, ...t) {
    const [o, s] = this._replaceStreamingParams(t), r = this._sendWithProtocol(this._createInvocation(e, t, !0, s));
    return this._launchStreams(o, r), r;
  }
  /** Invokes a hub method on the server using the specified name and arguments.
   *
   * The Promise returned by this method resolves when the server indicates it has finished invoking the method. When the promise
   * resolves, the server has finished invoking the method. If the server method returns a result, it is produced as the result of
   * resolving the Promise.
   *
   * @typeparam T The expected return type.
   * @param {string} methodName The name of the server method to invoke.
   * @param {any[]} args The arguments used to invoke the server method.
   * @returns {Promise<T>} A Promise that resolves with the result of the server method (if any), or rejects with an error.
   */
  invoke(e, ...t) {
    const [o, s] = this._replaceStreamingParams(t), r = this._createInvocation(e, t, !1, s);
    return new Promise((a, l) => {
      this._callbacks[r.invocationId] = (g, k) => {
        if (k) {
          l(k);
          return;
        } else g && (g.type === d.Completion ? g.error ? l(new Error(g.error)) : a(g.result) : l(new Error(`Unexpected message type: ${g.type}`)));
      };
      const u = this._sendWithProtocol(r).catch((g) => {
        l(g), delete this._callbacks[r.invocationId];
      });
      this._launchStreams(o, u);
    });
  }
  on(e, t) {
    !e || !t || (e = e.toLowerCase(), this._methods[e] || (this._methods[e] = []), this._methods[e].indexOf(t) === -1 && this._methods[e].push(t));
  }
  off(e, t) {
    if (!e)
      return;
    e = e.toLowerCase();
    const o = this._methods[e];
    if (o)
      if (t) {
        const s = o.indexOf(t);
        s !== -1 && (o.splice(s, 1), o.length === 0 && delete this._methods[e]);
      } else
        delete this._methods[e];
  }
  /** Registers a handler that will be invoked when the connection is closed.
   *
   * @param {Function} callback The handler that will be invoked when the connection is closed. Optionally receives a single argument containing the error that caused the connection to close (if any).
   */
  onclose(e) {
    e && this._closedCallbacks.push(e);
  }
  /** Registers a handler that will be invoked when the connection starts reconnecting.
   *
   * @param {Function} callback The handler that will be invoked when the connection starts reconnecting. Optionally receives a single argument containing the error that caused the connection to start reconnecting (if any).
   */
  onreconnecting(e) {
    e && this._reconnectingCallbacks.push(e);
  }
  /** Registers a handler that will be invoked when the connection successfully reconnects.
   *
   * @param {Function} callback The handler that will be invoked when the connection successfully reconnects.
   */
  onreconnected(e) {
    e && this._reconnectedCallbacks.push(e);
  }
  _processIncomingData(e) {
    if (this._cleanupTimeout(), this._receivedHandshakeResponse || (e = this._processHandshakeResponse(e), this._receivedHandshakeResponse = !0), e) {
      const t = this._protocol.parseMessages(e, this._logger);
      for (const o of t)
        if (!(this._messageBuffer && !this._messageBuffer._shouldProcessMessage(o)))
          switch (o.type) {
            case d.Invocation:
              this._invokeClientMethod(o).catch((s) => {
                this._logger.log(i.Error, `Invoke client method threw error: ${ie(s)}`);
              });
              break;
            case d.StreamItem:
            case d.Completion: {
              const s = this._callbacks[o.invocationId];
              if (s) {
                o.type === d.Completion && delete this._callbacks[o.invocationId];
                try {
                  s(o);
                } catch (r) {
                  this._logger.log(i.Error, `Stream callback threw error: ${ie(r)}`);
                }
              }
              break;
            }
            case d.Ping:
              break;
            case d.Close: {
              this._logger.log(i.Information, "Close message received from server.");
              const s = o.error ? new Error("Server returned an error on close: " + o.error) : void 0;
              o.allowReconnect === !0 ? this.connection.stop(s) : this._stopPromise = this._stopInternal(s);
              break;
            }
            case d.Ack:
              this._messageBuffer && this._messageBuffer._ack(o);
              break;
            case d.Sequence:
              this._messageBuffer && this._messageBuffer._resetSequence(o);
              break;
            default:
              this._logger.log(i.Warning, `Invalid message type: ${o.type}.`);
              break;
          }
    }
    this._resetTimeoutPeriod();
  }
  _processHandshakeResponse(e) {
    let t, o;
    try {
      [o, t] = this._handshakeProtocol.parseHandshakeResponse(e);
    } catch (s) {
      const r = "Error parsing handshake response: " + s;
      this._logger.log(i.Error, r);
      const c = new Error(r);
      throw this._handshakeRejecter(c), c;
    }
    if (t.error) {
      const s = "Server returned handshake error: " + t.error;
      this._logger.log(i.Error, s);
      const r = new Error(s);
      throw this._handshakeRejecter(r), r;
    } else
      this._logger.log(i.Debug, "Server handshake complete.");
    return this._handshakeResolver(), o;
  }
  _resetKeepAliveInterval() {
    this.connection.features.inherentKeepAlive || (this._nextKeepAlive = (/* @__PURE__ */ new Date()).getTime() + this.keepAliveIntervalInMilliseconds, this._cleanupPingTimer());
  }
  _resetTimeoutPeriod() {
    if ((!this.connection.features || !this.connection.features.inherentKeepAlive) && (this._timeoutHandle = setTimeout(() => this.serverTimeout(), this.serverTimeoutInMilliseconds), this._pingServerHandle === void 0)) {
      let e = this._nextKeepAlive - (/* @__PURE__ */ new Date()).getTime();
      e < 0 && (e = 0), this._pingServerHandle = setTimeout(async () => {
        if (this._connectionState === p.Connected)
          try {
            await this._sendMessage(this._cachedPingMessage);
          } catch {
            this._cleanupPingTimer();
          }
      }, e);
    }
  }
  // eslint-disable-next-line @typescript-eslint/naming-convention
  serverTimeout() {
    this.connection.stop(new Error("Server timeout elapsed without receiving a message from the server."));
  }
  async _invokeClientMethod(e) {
    const t = e.target.toLowerCase(), o = this._methods[t];
    if (!o) {
      this._logger.log(i.Warning, `No client method with the name '${t}' found.`), e.invocationId && (this._logger.log(i.Warning, `No result given for '${t}' method and invocation ID '${e.invocationId}'.`), await this._sendWithProtocol(this._createCompletionMessage(e.invocationId, "Client didn't provide a result.", null)));
      return;
    }
    const s = o.slice(), r = !!e.invocationId;
    let c, a, l;
    for (const u of s)
      try {
        const g = c;
        c = await u.apply(this, e.arguments), r && c && g && (this._logger.log(i.Error, `Multiple results provided for '${t}'. Sending error to server.`), l = this._createCompletionMessage(e.invocationId, "Client provided multiple results.", null)), a = void 0;
      } catch (g) {
        a = g, this._logger.log(i.Error, `A callback for the method '${t}' threw error '${g}'.`);
      }
    l ? await this._sendWithProtocol(l) : r ? (a ? l = this._createCompletionMessage(e.invocationId, `${a}`, null) : c !== void 0 ? l = this._createCompletionMessage(e.invocationId, null, c) : (this._logger.log(i.Warning, `No result given for '${t}' method and invocation ID '${e.invocationId}'.`), l = this._createCompletionMessage(e.invocationId, "Client didn't provide a result.", null)), await this._sendWithProtocol(l)) : c && this._logger.log(i.Error, `Result given for '${t}' method but server is not expecting a result.`);
  }
  _connectionClosed(e) {
    this._logger.log(i.Debug, `HubConnection.connectionClosed(${e}) called while in state ${this._connectionState}.`), this._stopDuringStartError = this._stopDuringStartError || e || new T("The underlying connection was closed before the hub handshake could complete."), this._handshakeResolver && this._handshakeResolver(), this._cancelCallbacksWithError(e || new Error("Invocation canceled due to the underlying connection being closed.")), this._cleanupTimeout(), this._cleanupPingTimer(), this._connectionState === p.Disconnecting ? this._completeClose(e) : this._connectionState === p.Connected && this._reconnectPolicy ? this._reconnect(e) : this._connectionState === p.Connected && this._completeClose(e);
  }
  _completeClose(e) {
    if (this._connectionStarted) {
      this._connectionState = p.Disconnected, this._connectionStarted = !1, this._messageBuffer && (this._messageBuffer._dispose(e ?? new Error("Connection closed.")), this._messageBuffer = void 0), _.isBrowser && window.document.removeEventListener("freeze", this._freezeEventListener);
      try {
        this._closedCallbacks.forEach((t) => t.apply(this, [e]));
      } catch (t) {
        this._logger.log(i.Error, `An onclose callback called with error '${e}' threw error '${t}'.`);
      }
    }
  }
  async _reconnect(e) {
    const t = Date.now();
    let o = 0, s = e !== void 0 ? e : new Error("Attempting to reconnect due to a unknown error."), r = this._getNextRetryDelay(o++, 0, s);
    if (r === null) {
      this._logger.log(i.Debug, "Connection not reconnecting because the IRetryPolicy returned null on the first reconnect attempt."), this._completeClose(e);
      return;
    }
    if (this._connectionState = p.Reconnecting, e ? this._logger.log(i.Information, `Connection reconnecting because of error '${e}'.`) : this._logger.log(i.Information, "Connection reconnecting."), this._reconnectingCallbacks.length !== 0) {
      try {
        this._reconnectingCallbacks.forEach((c) => c.apply(this, [e]));
      } catch (c) {
        this._logger.log(i.Error, `An onreconnecting callback called with error '${e}' threw error '${c}'.`);
      }
      if (this._connectionState !== p.Reconnecting) {
        this._logger.log(i.Debug, "Connection left the reconnecting state in onreconnecting callback. Done reconnecting.");
        return;
      }
    }
    for (; r !== null; ) {
      if (this._logger.log(i.Information, `Reconnect attempt number ${o} will start in ${r} ms.`), await new Promise((c) => {
        this._reconnectDelayHandle = setTimeout(c, r);
      }), this._reconnectDelayHandle = void 0, this._connectionState !== p.Reconnecting) {
        this._logger.log(i.Debug, "Connection left the reconnecting state during reconnect delay. Done reconnecting.");
        return;
      }
      try {
        if (await this._startInternal(), this._connectionState = p.Connected, this._logger.log(i.Information, "HubConnection reconnected successfully."), this._reconnectedCallbacks.length !== 0)
          try {
            this._reconnectedCallbacks.forEach((c) => c.apply(this, [this.connection.connectionId]));
          } catch (c) {
            this._logger.log(i.Error, `An onreconnected callback called with connectionId '${this.connection.connectionId}; threw error '${c}'.`);
          }
        return;
      } catch (c) {
        if (this._logger.log(i.Information, `Reconnect attempt failed because of error '${c}'.`), this._connectionState !== p.Reconnecting) {
          this._logger.log(i.Debug, `Connection moved to the '${this._connectionState}' from the reconnecting state during reconnect attempt. Done reconnecting.`), this._connectionState === p.Disconnecting && this._completeClose();
          return;
        }
        s = c instanceof Error ? c : new Error(c.toString()), r = this._getNextRetryDelay(o++, Date.now() - t, s);
      }
    }
    this._logger.log(i.Information, `Reconnect retries have been exhausted after ${Date.now() - t} ms and ${o} failed attempts. Connection disconnecting.`), this._completeClose();
  }
  _getNextRetryDelay(e, t, o) {
    try {
      return this._reconnectPolicy.nextRetryDelayInMilliseconds({
        elapsedMilliseconds: t,
        previousRetryCount: e,
        retryReason: o
      });
    } catch (s) {
      return this._logger.log(i.Error, `IRetryPolicy.nextRetryDelayInMilliseconds(${e}, ${t}) threw error '${s}'.`), null;
    }
  }
  _cancelCallbacksWithError(e) {
    const t = this._callbacks;
    this._callbacks = {}, Object.keys(t).forEach((o) => {
      const s = t[o];
      try {
        s(null, e);
      } catch (r) {
        this._logger.log(i.Error, `Stream 'error' callback called with '${e}' threw error: ${ie(r)}`);
      }
    });
  }
  _cleanupPingTimer() {
    this._pingServerHandle && (clearTimeout(this._pingServerHandle), this._pingServerHandle = void 0);
  }
  _cleanupTimeout() {
    this._timeoutHandle && clearTimeout(this._timeoutHandle);
  }
  _createInvocation(e, t, o, s) {
    if (o)
      return s.length !== 0 ? {
        arguments: t,
        streamIds: s,
        target: e,
        type: d.Invocation
      } : {
        arguments: t,
        target: e,
        type: d.Invocation
      };
    {
      const r = this._invocationId;
      return this._invocationId++, s.length !== 0 ? {
        arguments: t,
        invocationId: r.toString(),
        streamIds: s,
        target: e,
        type: d.Invocation
      } : {
        arguments: t,
        invocationId: r.toString(),
        target: e,
        type: d.Invocation
      };
    }
  }
  _launchStreams(e, t) {
    if (e.length !== 0) {
      t || (t = Promise.resolve());
      for (const o in e)
        e[o].subscribe({
          complete: () => {
            t = t.then(() => this._sendWithProtocol(this._createCompletionMessage(o)));
          },
          error: (s) => {
            let r;
            s instanceof Error ? r = s.message : s && s.toString ? r = s.toString() : r = "Unknown error", t = t.then(() => this._sendWithProtocol(this._createCompletionMessage(o, r)));
          },
          next: (s) => {
            t = t.then(() => this._sendWithProtocol(this._createStreamItemMessage(o, s)));
          }
        });
    }
  }
  _replaceStreamingParams(e) {
    const t = [], o = [];
    for (let s = 0; s < e.length; s++) {
      const r = e[s];
      if (this._isObservable(r)) {
        const c = this._invocationId;
        this._invocationId++, t[c] = r, o.push(c.toString()), e.splice(s, 1);
      }
    }
    return [t, o];
  }
  _isObservable(e) {
    return e && e.subscribe && typeof e.subscribe == "function";
  }
  _createStreamInvocation(e, t, o) {
    const s = this._invocationId;
    return this._invocationId++, o.length !== 0 ? {
      arguments: t,
      invocationId: s.toString(),
      streamIds: o,
      target: e,
      type: d.StreamInvocation
    } : {
      arguments: t,
      invocationId: s.toString(),
      target: e,
      type: d.StreamInvocation
    };
  }
  _createCancelInvocation(e) {
    return {
      invocationId: e,
      type: d.CancelInvocation
    };
  }
  _createStreamItemMessage(e, t) {
    return {
      invocationId: e,
      item: t,
      type: d.StreamItem
    };
  }
  _createCompletionMessage(e, t, o) {
    return t ? {
      error: t,
      invocationId: e,
      type: d.Completion
    } : {
      invocationId: e,
      result: o,
      type: d.Completion
    };
  }
  _createCloseMessage() {
    return { type: d.Close };
  }
}
const _t = [0, 2e3, 1e4, 3e4, null];
class ve {
  constructor(e) {
    this._retryDelays = e !== void 0 ? [...e, null] : _t;
  }
  nextRetryDelayInMilliseconds(e) {
    return this._retryDelays[e.previousRetryCount];
  }
}
class $ {
}
$.Authorization = "Authorization";
$.Cookie = "Cookie";
class mt extends Z {
  constructor(e, t) {
    super(), this._innerClient = e, this._accessTokenFactory = t;
  }
  async send(e) {
    let t = !0;
    this._accessTokenFactory && (!this._accessToken || e.url && e.url.indexOf("/negotiate?") > 0) && (t = !1, this._accessToken = await this._accessTokenFactory()), this._setAuthorizationHeader(e);
    const o = await this._innerClient.send(e);
    return t && o.statusCode === 401 && this._accessTokenFactory ? (this._accessToken = await this._accessTokenFactory(), this._setAuthorizationHeader(e), await this._innerClient.send(e)) : o;
  }
  _setAuthorizationHeader(e) {
    e.headers || (e.headers = {}), this._accessToken ? e.headers[$.Authorization] = `Bearer ${this._accessToken}` : this._accessTokenFactory && e.headers[$.Authorization] && delete e.headers[$.Authorization];
  }
  getCookieString(e) {
    return this._innerClient.getCookieString(e);
  }
}
var w;
(function(n) {
  n[n.None = 0] = "None", n[n.WebSockets = 1] = "WebSockets", n[n.ServerSentEvents = 2] = "ServerSentEvents", n[n.LongPolling = 4] = "LongPolling";
})(w || (w = {}));
var S;
(function(n) {
  n[n.Text = 1] = "Text", n[n.Binary = 2] = "Binary";
})(S || (S = {}));
let bt = class {
  constructor() {
    this._isAborted = !1, this.onabort = null;
  }
  abort() {
    this._isAborted || (this._isAborted = !0, this.onabort && this.onabort());
  }
  get signal() {
    return this;
  }
  get aborted() {
    return this._isAborted;
  }
};
class ye {
  // This is an internal type, not exported from 'index' so this is really just internal.
  get pollAborted() {
    return this._pollAbort.aborted;
  }
  constructor(e, t, o) {
    this._httpClient = e, this._logger = t, this._pollAbort = new bt(), this._options = o, this._running = !1, this.onreceive = null, this.onclose = null;
  }
  async connect(e, t) {
    if (m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, S, "transferFormat"), this._url = e, this._logger.log(i.Trace, "(LongPolling transport) Connecting."), t === S.Binary && typeof XMLHttpRequest < "u" && typeof new XMLHttpRequest().responseType != "string")
      throw new Error("Binary protocols over XmlHttpRequest not implementing advanced features are not supported.");
    const [o, s] = M(), r = { [o]: s, ...this._options.headers }, c = {
      abortSignal: this._pollAbort.signal,
      headers: r,
      timeout: 1e5,
      withCredentials: this._options.withCredentials
    };
    t === S.Binary && (c.responseType = "arraybuffer");
    const a = `${e}&_=${Date.now()}`;
    this._logger.log(i.Trace, `(LongPolling transport) polling: ${a}.`);
    const l = await this._httpClient.get(a, c);
    l.statusCode !== 200 ? (this._logger.log(i.Error, `(LongPolling transport) Unexpected response code: ${l.statusCode}.`), this._closeError = new R(l.statusText || "", l.statusCode), this._running = !1) : this._running = !0, this._receiving = this._poll(this._url, c);
  }
  async _poll(e, t) {
    try {
      for (; this._running; )
        try {
          const o = `${e}&_=${Date.now()}`;
          this._logger.log(i.Trace, `(LongPolling transport) polling: ${o}.`);
          const s = await this._httpClient.get(o, t);
          s.statusCode === 204 ? (this._logger.log(i.Information, "(LongPolling transport) Poll terminated by server."), this._running = !1) : s.statusCode !== 200 ? (this._logger.log(i.Error, `(LongPolling transport) Unexpected response code: ${s.statusCode}.`), this._closeError = new R(s.statusText || "", s.statusCode), this._running = !1) : s.content ? (this._logger.log(i.Trace, `(LongPolling transport) data received. ${F(s.content, this._options.logMessageContent)}.`), this.onreceive && this.onreceive(s.content)) : this._logger.log(i.Trace, "(LongPolling transport) Poll timed out, reissuing.");
        } catch (o) {
          this._running ? o instanceof le ? this._logger.log(i.Trace, "(LongPolling transport) Poll timed out, reissuing.") : (this._closeError = o, this._running = !1) : this._logger.log(i.Trace, `(LongPolling transport) Poll errored after shutdown: ${o.message}`);
        }
    } finally {
      this._logger.log(i.Trace, "(LongPolling transport) Polling complete."), this.pollAborted || this._raiseOnClose();
    }
  }
  async send(e) {
    return this._running ? $e(this._logger, "LongPolling", this._httpClient, this._url, e, this._options) : Promise.reject(new Error("Cannot send until the transport is connected"));
  }
  async stop() {
    this._logger.log(i.Trace, "(LongPolling transport) Stopping polling."), this._running = !1, this._pollAbort.abort();
    try {
      await this._receiving, this._logger.log(i.Trace, `(LongPolling transport) sending DELETE request to ${this._url}.`);
      const e = {}, [t, o] = M();
      e[t] = o;
      const s = {
        headers: { ...e, ...this._options.headers },
        timeout: this._options.timeout,
        withCredentials: this._options.withCredentials
      };
      let r;
      try {
        await this._httpClient.delete(this._url, s);
      } catch (c) {
        r = c;
      }
      r ? r instanceof R && (r.statusCode === 404 ? this._logger.log(i.Trace, "(LongPolling transport) A 404 response was returned from sending a DELETE request.") : this._logger.log(i.Trace, `(LongPolling transport) Error sending a DELETE request: ${r}`)) : this._logger.log(i.Trace, "(LongPolling transport) DELETE request accepted.");
    } finally {
      this._logger.log(i.Trace, "(LongPolling transport) Stop finished."), this._raiseOnClose();
    }
  }
  _raiseOnClose() {
    if (this.onclose) {
      let e = "(LongPolling transport) Firing onclose event.";
      this._closeError && (e += " Error: " + this._closeError), this._logger.log(i.Trace, e), this.onclose(this._closeError);
    }
  }
}
class wt {
  constructor(e, t, o, s) {
    this._httpClient = e, this._accessToken = t, this._logger = o, this._options = s, this.onreceive = null, this.onclose = null;
  }
  async connect(e, t) {
    return m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, S, "transferFormat"), this._logger.log(i.Trace, "(SSE transport) Connecting."), this._url = e, this._accessToken && (e += (e.indexOf("?") < 0 ? "?" : "&") + `access_token=${encodeURIComponent(this._accessToken)}`), new Promise((o, s) => {
      let r = !1;
      if (t !== S.Text) {
        s(new Error("The Server-Sent Events transport only supports the 'Text' transfer format"));
        return;
      }
      let c;
      if (_.isBrowser || _.isWebWorker)
        c = new this._options.EventSource(e, { withCredentials: this._options.withCredentials });
      else {
        const a = this._httpClient.getCookieString(e), l = {};
        l.Cookie = a;
        const [u, g] = M();
        l[u] = g, c = new this._options.EventSource(e, { withCredentials: this._options.withCredentials, headers: { ...l, ...this._options.headers } });
      }
      try {
        c.onmessage = (a) => {
          if (this.onreceive)
            try {
              this._logger.log(i.Trace, `(SSE transport) data received. ${F(a.data, this._options.logMessageContent)}.`), this.onreceive(a.data);
            } catch (l) {
              this._close(l);
              return;
            }
        }, c.onerror = (a) => {
          r ? this._close() : s(new Error("EventSource failed to connect. The connection could not be found on the server, either the connection ID is not present on the server, or a proxy is refusing/buffering the connection. If you have multiple servers check that sticky sessions are enabled."));
        }, c.onopen = () => {
          this._logger.log(i.Information, `SSE connected to ${this._url}`), this._eventSource = c, r = !0, o();
        };
      } catch (a) {
        s(a);
        return;
      }
    });
  }
  async send(e) {
    return this._eventSource ? $e(this._logger, "SSE", this._httpClient, this._url, e, this._options) : Promise.reject(new Error("Cannot send until the transport is connected"));
  }
  stop() {
    return this._close(), Promise.resolve();
  }
  _close(e) {
    this._eventSource && (this._eventSource.close(), this._eventSource = void 0, this.onclose && this.onclose(e));
  }
}
class St {
  constructor(e, t, o, s, r, c) {
    this._logger = o, this._accessTokenFactory = t, this._logMessageContent = s, this._webSocketConstructor = r, this._httpClient = e, this.onreceive = null, this.onclose = null, this._headers = c;
  }
  async connect(e, t) {
    m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, S, "transferFormat"), this._logger.log(i.Trace, "(WebSockets transport) Connecting.");
    let o;
    return this._accessTokenFactory && (o = await this._accessTokenFactory()), new Promise((s, r) => {
      e = e.replace(/^http/, "ws");
      let c;
      const a = this._httpClient.getCookieString(e);
      let l = !1;
      if (_.isNode || _.isReactNative) {
        const u = {}, [g, k] = M();
        u[g] = k, o && (u[$.Authorization] = `Bearer ${o}`), a && (u[$.Cookie] = a), c = new this._webSocketConstructor(e, void 0, {
          headers: { ...u, ...this._headers }
        });
      } else
        o && (e += (e.indexOf("?") < 0 ? "?" : "&") + `access_token=${encodeURIComponent(o)}`);
      c || (c = new this._webSocketConstructor(e)), t === S.Binary && (c.binaryType = "arraybuffer"), c.onopen = (u) => {
        this._logger.log(i.Information, `WebSocket connected to ${e}.`), this._webSocket = c, l = !0, s();
      }, c.onerror = (u) => {
        let g = null;
        typeof ErrorEvent < "u" && u instanceof ErrorEvent ? g = u.error : g = "There was an error with the transport", this._logger.log(i.Information, `(WebSockets transport) ${g}.`);
      }, c.onmessage = (u) => {
        if (this._logger.log(i.Trace, `(WebSockets transport) data received. ${F(u.data, this._logMessageContent)}.`), this.onreceive)
          try {
            this.onreceive(u.data);
          } catch (g) {
            this._close(g);
            return;
          }
      }, c.onclose = (u) => {
        if (l)
          this._close(u);
        else {
          let g = null;
          typeof ErrorEvent < "u" && u instanceof ErrorEvent ? g = u.error : g = "WebSocket failed to connect. The connection could not be found on the server, either the endpoint may not be a SignalR endpoint, the connection ID is not present on the server, or there is a proxy blocking WebSockets. If you have multiple servers check that sticky sessions are enabled.", r(new Error(g));
        }
      };
    });
  }
  send(e) {
    return this._webSocket && this._webSocket.readyState === this._webSocketConstructor.OPEN ? (this._logger.log(i.Trace, `(WebSockets transport) sending data. ${F(e, this._logMessageContent)}.`), this._webSocket.send(e), Promise.resolve()) : Promise.reject("WebSocket is not in the OPEN state");
  }
  stop() {
    return this._webSocket && this._close(void 0), Promise.resolve();
  }
  _close(e) {
    this._webSocket && (this._webSocket.onclose = () => {
    }, this._webSocket.onmessage = () => {
    }, this._webSocket.onerror = () => {
    }, this._webSocket.close(), this._webSocket = void 0), this._logger.log(i.Trace, "(WebSockets transport) socket closed."), this.onclose && (this._isCloseEvent(e) && (e.wasClean === !1 || e.code !== 1e3) ? this.onclose(new Error(`WebSocket closed with status code: ${e.code} (${e.reason || "no reason given"}).`)) : e instanceof Error ? this.onclose(e) : this.onclose());
  }
  _isCloseEvent(e) {
    return e && typeof e.wasClean == "boolean" && typeof e.code == "number";
  }
}
const ke = 100;
class Ct {
  constructor(e, t = {}) {
    if (this._stopPromiseResolver = () => {
    }, this.features = {}, this._negotiateVersion = 1, m.isRequired(e, "url"), this._logger = Qe(t.logger), this.baseUrl = this._resolveUrl(e), t = t || {}, t.logMessageContent = t.logMessageContent === void 0 ? !1 : t.logMessageContent, typeof t.withCredentials == "boolean" || t.withCredentials === void 0)
      t.withCredentials = t.withCredentials === void 0 ? !0 : t.withCredentials;
    else
      throw new Error("withCredentials option was not a 'boolean' or 'undefined' value");
    t.timeout = t.timeout === void 0 ? 100 * 1e3 : t.timeout;
    let o = null, s = null;
    if (_.isNode && typeof require < "u") {
      const r = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      o = r("ws"), s = r("eventsource");
    }
    !_.isNode && typeof WebSocket < "u" && !t.WebSocket ? t.WebSocket = WebSocket : _.isNode && !t.WebSocket && o && (t.WebSocket = o), !_.isNode && typeof EventSource < "u" && !t.EventSource ? t.EventSource = EventSource : _.isNode && !t.EventSource && typeof s < "u" && (t.EventSource = s), this._httpClient = new mt(t.httpClient || new at(this._logger), t.accessTokenFactory), this._connectionState = "Disconnected", this._connectionStarted = !1, this._options = t, this.onreceive = null, this.onclose = null;
  }
  async start(e) {
    if (e = e || S.Binary, m.isIn(e, S, "transferFormat"), this._logger.log(i.Debug, `Starting connection with transfer format '${S[e]}'.`), this._connectionState !== "Disconnected")
      return Promise.reject(new Error("Cannot start an HttpConnection that is not in the 'Disconnected' state."));
    if (this._connectionState = "Connecting", this._startInternalPromise = this._startInternal(e), await this._startInternalPromise, this._connectionState === "Disconnecting") {
      const t = "Failed to start the HttpConnection before stop() was called.";
      return this._logger.log(i.Error, t), await this._stopPromise, Promise.reject(new T(t));
    } else if (this._connectionState !== "Connected") {
      const t = "HttpConnection.startInternal completed gracefully but didn't enter the connection into the connected state!";
      return this._logger.log(i.Error, t), Promise.reject(new T(t));
    }
    this._connectionStarted = !0;
  }
  send(e) {
    return this._connectionState !== "Connected" ? Promise.reject(new Error("Cannot send data if the connection is not in the 'Connected' State.")) : (this._sendQueue || (this._sendQueue = new ue(this.transport)), this._sendQueue.send(e));
  }
  async stop(e) {
    if (this._connectionState === "Disconnected")
      return this._logger.log(i.Debug, `Call to HttpConnection.stop(${e}) ignored because the connection is already in the disconnected state.`), Promise.resolve();
    if (this._connectionState === "Disconnecting")
      return this._logger.log(i.Debug, `Call to HttpConnection.stop(${e}) ignored because the connection is already in the disconnecting state.`), this._stopPromise;
    this._connectionState = "Disconnecting", this._stopPromise = new Promise((t) => {
      this._stopPromiseResolver = t;
    }), await this._stopInternal(e), await this._stopPromise;
  }
  async _stopInternal(e) {
    this._stopError = e;
    try {
      await this._startInternalPromise;
    } catch {
    }
    if (this.transport) {
      try {
        await this.transport.stop();
      } catch (t) {
        this._logger.log(i.Error, `HttpConnection.transport.stop() threw error '${t}'.`), this._stopConnection();
      }
      this.transport = void 0;
    } else
      this._logger.log(i.Debug, "HttpConnection.transport is undefined in HttpConnection.stop() because start() failed.");
  }
  async _startInternal(e) {
    let t = this.baseUrl;
    this._accessTokenFactory = this._options.accessTokenFactory, this._httpClient._accessTokenFactory = this._accessTokenFactory;
    try {
      if (this._options.skipNegotiation)
        if (this._options.transport === w.WebSockets)
          this.transport = this._constructTransport(w.WebSockets), await this._startTransport(t, e);
        else
          throw new Error("Negotiation can only be skipped when using the WebSocket transport directly.");
      else {
        let o = null, s = 0;
        do {
          if (o = await this._getNegotiationResponse(t), this._connectionState === "Disconnecting" || this._connectionState === "Disconnected")
            throw new T("The connection was stopped during negotiation.");
          if (o.error)
            throw new Error(o.error);
          if (o.ProtocolVersion)
            throw new Error("Detected a connection attempt to an ASP.NET SignalR Server. This client only supports connecting to an ASP.NET Core SignalR Server. See https://aka.ms/signalr-core-differences for details.");
          if (o.url && (t = o.url), o.accessToken) {
            const r = o.accessToken;
            this._accessTokenFactory = () => r, this._httpClient._accessToken = r, this._httpClient._accessTokenFactory = void 0;
          }
          s++;
        } while (o.url && s < ke);
        if (s === ke && o.url)
          throw new Error("Negotiate redirection limit exceeded.");
        await this._createTransport(t, this._options.transport, o, e);
      }
      this.transport instanceof ye && (this.features.inherentKeepAlive = !0), this._connectionState === "Connecting" && (this._logger.log(i.Debug, "The HttpConnection connected successfully."), this._connectionState = "Connected");
    } catch (o) {
      return this._logger.log(i.Error, "Failed to start the connection: " + o), this._connectionState = "Disconnected", this.transport = void 0, this._stopPromiseResolver(), Promise.reject(o);
    }
  }
  async _getNegotiationResponse(e) {
    const t = {}, [o, s] = M();
    t[o] = s;
    const r = this._resolveNegotiateUrl(e);
    this._logger.log(i.Debug, `Sending negotiation request: ${r}.`);
    try {
      const c = await this._httpClient.post(r, {
        content: "",
        headers: { ...t, ...this._options.headers },
        timeout: this._options.timeout,
        withCredentials: this._options.withCredentials
      });
      if (c.statusCode !== 200)
        return Promise.reject(new Error(`Unexpected status code returned from negotiate '${c.statusCode}'`));
      const a = JSON.parse(c.content);
      return (!a.negotiateVersion || a.negotiateVersion < 1) && (a.connectionToken = a.connectionId), a.useStatefulReconnect && this._options._useStatefulReconnect !== !0 ? Promise.reject(new Se("Client didn't negotiate Stateful Reconnect but the server did.")) : a;
    } catch (c) {
      let a = "Failed to complete negotiation with the server: " + c;
      return c instanceof R && c.statusCode === 404 && (a = a + " Either this is not a SignalR endpoint or there is a proxy blocking the connection."), this._logger.log(i.Error, a), Promise.reject(new Se(a));
    }
  }
  _createConnectUrl(e, t) {
    return t ? e + (e.indexOf("?") === -1 ? "?" : "&") + `id=${t}` : e;
  }
  async _createTransport(e, t, o, s) {
    let r = this._createConnectUrl(e, o.connectionToken);
    if (this._isITransport(t)) {
      this._logger.log(i.Debug, "Connection was provided an instance of ITransport, using that directly."), this.transport = t, await this._startTransport(r, s), this.connectionId = o.connectionId;
      return;
    }
    const c = [], a = o.availableTransports || [];
    let l = o;
    for (const u of a) {
      const g = this._resolveTransportOrError(u, t, s, l?.useStatefulReconnect === !0);
      if (g instanceof Error)
        c.push(`${u.transport} failed:`), c.push(g);
      else if (this._isITransport(g)) {
        if (this.transport = g, !l) {
          try {
            l = await this._getNegotiationResponse(e);
          } catch (k) {
            return Promise.reject(k);
          }
          r = this._createConnectUrl(e, l.connectionToken);
        }
        try {
          await this._startTransport(r, s), this.connectionId = l.connectionId;
          return;
        } catch (k) {
          if (this._logger.log(i.Error, `Failed to start the transport '${u.transport}': ${k}`), l = void 0, c.push(new Ke(`${u.transport} failed: ${k}`, w[u.transport])), this._connectionState !== "Connecting") {
            const A = "Failed to select transport before stop() was called.";
            return this._logger.log(i.Debug, A), Promise.reject(new T(A));
          }
        }
      }
    }
    return c.length > 0 ? Promise.reject(new Ge(`Unable to connect to the server with any of the available transports. ${c.join(" ")}`, c)) : Promise.reject(new Error("None of the transports supported by the client are supported by the server."));
  }
  _constructTransport(e) {
    switch (e) {
      case w.WebSockets:
        if (!this._options.WebSocket)
          throw new Error("'WebSocket' is not supported in your environment.");
        return new St(this._httpClient, this._accessTokenFactory, this._logger, this._options.logMessageContent, this._options.WebSocket, this._options.headers || {});
      case w.ServerSentEvents:
        if (!this._options.EventSource)
          throw new Error("'EventSource' is not supported in your environment.");
        return new wt(this._httpClient, this._httpClient._accessToken, this._logger, this._options);
      case w.LongPolling:
        return new ye(this._httpClient, this._logger, this._options);
      default:
        throw new Error(`Unknown transport: ${e}.`);
    }
  }
  _startTransport(e, t) {
    return this.transport.onreceive = this.onreceive, this.features.reconnect ? this.transport.onclose = async (o) => {
      let s = !1;
      if (this.features.reconnect)
        try {
          this.features.disconnected(), await this.transport.connect(e, t), await this.features.resend();
        } catch {
          s = !0;
        }
      else {
        this._stopConnection(o);
        return;
      }
      s && this._stopConnection(o);
    } : this.transport.onclose = (o) => this._stopConnection(o), this.transport.connect(e, t);
  }
  _resolveTransportOrError(e, t, o, s) {
    const r = w[e.transport];
    if (r == null)
      return this._logger.log(i.Debug, `Skipping transport '${e.transport}' because it is not supported by this client.`), new Error(`Skipping transport '${e.transport}' because it is not supported by this client.`);
    if (vt(t, r))
      if (e.transferFormats.map((a) => S[a]).indexOf(o) >= 0) {
        if (r === w.WebSockets && !this._options.WebSocket || r === w.ServerSentEvents && !this._options.EventSource)
          return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it is not supported in your environment.'`), new Xe(`'${w[r]}' is not supported in your environment.`, r);
        this._logger.log(i.Debug, `Selecting transport '${w[r]}'.`);
        try {
          return this.features.reconnect = r === w.WebSockets ? s : void 0, this._constructTransport(r);
        } catch (a) {
          return a;
        }
      } else
        return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it does not support the requested transfer format '${S[o]}'.`), new Error(`'${w[r]}' does not support ${S[o]}.`);
    else
      return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it was disabled by the client.`), new Ve(`'${w[r]}' is disabled by the client.`, r);
  }
  _isITransport(e) {
    return e && typeof e == "object" && "connect" in e;
  }
  _stopConnection(e) {
    if (this._logger.log(i.Debug, `HttpConnection.stopConnection(${e}) called while in state ${this._connectionState}.`), this.transport = void 0, e = this._stopError || e, this._stopError = void 0, this._connectionState === "Disconnected") {
      this._logger.log(i.Debug, `Call to HttpConnection.stopConnection(${e}) was ignored because the connection is already in the disconnected state.`);
      return;
    }
    if (this._connectionState === "Connecting")
      throw this._logger.log(i.Warning, `Call to HttpConnection.stopConnection(${e}) was ignored because the connection is still in the connecting state.`), new Error(`HttpConnection.stopConnection(${e}) was called while the connection is still in the connecting state.`);
    if (this._connectionState === "Disconnecting" && this._stopPromiseResolver(), e ? this._logger.log(i.Error, `Connection disconnected with error '${e}'.`) : this._logger.log(i.Information, "Connection disconnected."), this._sendQueue && (this._sendQueue.stop().catch((t) => {
      this._logger.log(i.Error, `TransportSendQueue.stop() threw error '${t}'.`);
    }), this._sendQueue = void 0), this.connectionId = void 0, this._connectionState = "Disconnected", this._connectionStarted) {
      this._connectionStarted = !1;
      try {
        this.onclose && this.onclose(e);
      } catch (t) {
        this._logger.log(i.Error, `HttpConnection.onclose(${e}) threw error '${t}'.`);
      }
    }
  }
  _resolveUrl(e) {
    if (e.lastIndexOf("https://", 0) === 0 || e.lastIndexOf("http://", 0) === 0)
      return e;
    if (!_.isBrowser)
      throw new Error(`Cannot resolve '${e}'.`);
    const t = window.document.createElement("a");
    return t.href = e, this._logger.log(i.Information, `Normalizing '${e}' to '${t.href}'.`), t.href;
  }
  _resolveNegotiateUrl(e) {
    const t = new URL(e);
    t.pathname.endsWith("/") ? t.pathname += "negotiate" : t.pathname += "/negotiate";
    const o = new URLSearchParams(t.searchParams);
    return o.has("negotiateVersion") || o.append("negotiateVersion", this._negotiateVersion.toString()), o.has("useStatefulReconnect") ? o.get("useStatefulReconnect") === "true" && (this._options._useStatefulReconnect = !0) : this._options._useStatefulReconnect === !0 && o.append("useStatefulReconnect", "true"), t.search = o.toString(), t.toString();
  }
}
function vt(n, e) {
  return !n || (e & n) !== 0;
}
class ue {
  constructor(e) {
    this._transport = e, this._buffer = [], this._executing = !0, this._sendBufferedData = new K(), this._transportResult = new K(), this._sendLoopPromise = this._sendLoop();
  }
  send(e) {
    return this._bufferData(e), this._transportResult || (this._transportResult = new K()), this._transportResult.promise;
  }
  stop() {
    return this._executing = !1, this._sendBufferedData.resolve(), this._sendLoopPromise;
  }
  _bufferData(e) {
    if (this._buffer.length && typeof this._buffer[0] != typeof e)
      throw new Error(`Expected data to be of type ${typeof this._buffer} but was of type ${typeof e}`);
    this._buffer.push(e), this._sendBufferedData.resolve();
  }
  async _sendLoop() {
    for (; ; ) {
      if (await this._sendBufferedData.promise, !this._executing) {
        this._transportResult && this._transportResult.reject("Connection stopped.");
        break;
      }
      this._sendBufferedData = new K();
      const e = this._transportResult;
      this._transportResult = void 0;
      const t = typeof this._buffer[0] == "string" ? this._buffer.join("") : ue._concatBuffers(this._buffer);
      this._buffer.length = 0;
      try {
        await this._transport.send(t), e.resolve();
      } catch (o) {
        e.reject(o);
      }
    }
  }
  static _concatBuffers(e) {
    const t = e.map((r) => r.byteLength).reduce((r, c) => r + c), o = new Uint8Array(t);
    let s = 0;
    for (const r of e)
      o.set(new Uint8Array(r), s), s += r.byteLength;
    return o.buffer;
  }
}
class K {
  constructor() {
    this.promise = new Promise((e, t) => [this._resolver, this._rejecter] = [e, t]);
  }
  resolve() {
    this._resolver();
  }
  reject(e) {
    this._rejecter(e);
  }
}
const yt = "json";
class kt {
  constructor() {
    this.name = yt, this.version = 2, this.transferFormat = S.Text;
  }
  /** Creates an array of {@link @microsoft/signalr.HubMessage} objects from the specified serialized representation.
   *
   * @param {string} input A string containing the serialized representation.
   * @param {ILogger} logger A logger that will be used to log messages that occur during parsing.
   */
  parseMessages(e, t) {
    if (typeof e != "string")
      throw new Error("Invalid input for JSON hub protocol. Expected a string.");
    if (!e)
      return [];
    t === null && (t = O.instance);
    const o = y.parse(e), s = [];
    for (const r of o) {
      const c = JSON.parse(r);
      if (typeof c.type != "number")
        throw new Error("Invalid payload.");
      switch (c.type) {
        case d.Invocation:
          this._isInvocationMessage(c);
          break;
        case d.StreamItem:
          this._isStreamItemMessage(c);
          break;
        case d.Completion:
          this._isCompletionMessage(c);
          break;
        case d.Ping:
          break;
        case d.Close:
          break;
        case d.Ack:
          this._isAckMessage(c);
          break;
        case d.Sequence:
          this._isSequenceMessage(c);
          break;
        default:
          t.log(i.Information, "Unknown message type '" + c.type + "' ignored.");
          continue;
      }
      s.push(c);
    }
    return s;
  }
  /** Writes the specified {@link @microsoft/signalr.HubMessage} to a string and returns it.
   *
   * @param {HubMessage} message The message to write.
   * @returns {string} A string containing the serialized representation of the message.
   */
  writeMessage(e) {
    return y.write(JSON.stringify(e));
  }
  _isInvocationMessage(e) {
    this._assertNotEmptyString(e.target, "Invalid payload for Invocation message."), e.invocationId !== void 0 && this._assertNotEmptyString(e.invocationId, "Invalid payload for Invocation message.");
  }
  _isStreamItemMessage(e) {
    if (this._assertNotEmptyString(e.invocationId, "Invalid payload for StreamItem message."), e.item === void 0)
      throw new Error("Invalid payload for StreamItem message.");
  }
  _isCompletionMessage(e) {
    if (e.result && e.error)
      throw new Error("Invalid payload for Completion message.");
    !e.result && e.error && this._assertNotEmptyString(e.error, "Invalid payload for Completion message."), this._assertNotEmptyString(e.invocationId, "Invalid payload for Completion message.");
  }
  _isAckMessage(e) {
    if (typeof e.sequenceId != "number")
      throw new Error("Invalid SequenceId for Ack message.");
  }
  _isSequenceMessage(e) {
    if (typeof e.sequenceId != "number")
      throw new Error("Invalid SequenceId for Sequence message.");
  }
  _assertNotEmptyString(e, t) {
    if (typeof e != "string" || e === "")
      throw new Error(t);
  }
}
const Et = {
  trace: i.Trace,
  debug: i.Debug,
  info: i.Information,
  information: i.Information,
  warn: i.Warning,
  warning: i.Warning,
  error: i.Error,
  critical: i.Critical,
  none: i.None
};
function It(n) {
  const e = Et[n.toLowerCase()];
  if (typeof e < "u")
    return e;
  throw new Error(`Unknown log level: ${n}`);
}
class Tt {
  configureLogging(e) {
    if (m.isRequired(e, "logging"), Pt(e))
      this.logger = e;
    else if (typeof e == "string") {
      const t = It(e);
      this.logger = new Y(t);
    } else
      this.logger = new Y(e);
    return this;
  }
  withUrl(e, t) {
    return m.isRequired(e, "url"), m.isNotEmpty(e, "url"), this.url = e, typeof t == "object" ? this.httpConnectionOptions = { ...this.httpConnectionOptions, ...t } : this.httpConnectionOptions = {
      ...this.httpConnectionOptions,
      transport: t
    }, this;
  }
  /** Configures the {@link @microsoft/signalr.HubConnection} to use the specified Hub Protocol.
   *
   * @param {IHubProtocol} protocol The {@link @microsoft/signalr.IHubProtocol} implementation to use.
   */
  withHubProtocol(e) {
    return m.isRequired(e, "protocol"), this.protocol = e, this;
  }
  withAutomaticReconnect(e) {
    if (this.reconnectPolicy)
      throw new Error("A reconnectPolicy has already been set.");
    return e ? Array.isArray(e) ? this.reconnectPolicy = new ve(e) : this.reconnectPolicy = e : this.reconnectPolicy = new ve(), this;
  }
  /** Configures {@link @microsoft/signalr.HubConnection.serverTimeoutInMilliseconds} for the {@link @microsoft/signalr.HubConnection}.
   *
   * @returns The {@link @microsoft/signalr.HubConnectionBuilder} instance, for chaining.
   */
  withServerTimeout(e) {
    return m.isRequired(e, "milliseconds"), this._serverTimeoutInMilliseconds = e, this;
  }
  /** Configures {@link @microsoft/signalr.HubConnection.keepAliveIntervalInMilliseconds} for the {@link @microsoft/signalr.HubConnection}.
   *
   * @returns The {@link @microsoft/signalr.HubConnectionBuilder} instance, for chaining.
   */
  withKeepAliveInterval(e) {
    return m.isRequired(e, "milliseconds"), this._keepAliveIntervalInMilliseconds = e, this;
  }
  /** Enables and configures options for the Stateful Reconnect feature.
   *
   * @returns The {@link @microsoft/signalr.HubConnectionBuilder} instance, for chaining.
   */
  withStatefulReconnect(e) {
    return this.httpConnectionOptions === void 0 && (this.httpConnectionOptions = {}), this.httpConnectionOptions._useStatefulReconnect = !0, this._statefulReconnectBufferSize = e?.bufferSize, this;
  }
  /** Creates a {@link @microsoft/signalr.HubConnection} from the configuration options specified in this builder.
   *
   * @returns {HubConnection} The configured {@link @microsoft/signalr.HubConnection}.
   */
  build() {
    const e = this.httpConnectionOptions || {};
    if (e.logger === void 0 && (e.logger = this.logger), !this.url)
      throw new Error("The 'HubConnectionBuilder.withUrl' method must be called before building the connection.");
    const t = new Ct(this.url, e);
    return he.create(t, this.logger || O.instance, this.protocol || new kt(), this.reconnectPolicy, this._serverTimeoutInMilliseconds, this._keepAliveIntervalInMilliseconds, this._statefulReconnectBufferSize);
  }
}
function Pt(n) {
  return n.log !== void 0;
}
const De = {
  server: { id: "server", label: "Server", endpointPrefix: "/_elsa/server/diagnostics/console-logs" },
  studio: { id: "studio", label: "Studio", endpointPrefix: "/_elsa/studio/diagnostics/console-logs" }
}, xt = "studio", ce = 2e3, Rt = 12e4, $t = 15e3, Dt = "Server timeout elapsed without receiving a message from the server.", Ae = "elsa-studio-console-stream-autoscroll", G = /\x1b\[([0-9;]*)m/g, Ee = {
  30: "console-stream-ansi-fg-black",
  31: "console-stream-ansi-fg-red",
  32: "console-stream-ansi-fg-green",
  33: "console-stream-ansi-fg-yellow",
  34: "console-stream-ansi-fg-blue",
  35: "console-stream-ansi-fg-magenta",
  36: "console-stream-ansi-fg-cyan",
  37: "console-stream-ansi-fg-white",
  90: "console-stream-ansi-fg-bright-black",
  91: "console-stream-ansi-fg-bright-red",
  92: "console-stream-ansi-fg-bright-green",
  93: "console-stream-ansi-fg-bright-yellow",
  94: "console-stream-ansi-fg-bright-blue",
  95: "console-stream-ansi-fg-bright-magenta",
  96: "console-stream-ansi-fg-bright-cyan",
  97: "console-stream-ansi-fg-bright-white"
}, Ie = {
  40: "console-stream-ansi-bg-black",
  41: "console-stream-ansi-bg-red",
  42: "console-stream-ansi-bg-green",
  43: "console-stream-ansi-bg-yellow",
  44: "console-stream-ansi-bg-blue",
  45: "console-stream-ansi-bg-magenta",
  46: "console-stream-ansi-bg-cyan",
  47: "console-stream-ansi-bg-white",
  100: "console-stream-ansi-bg-bright-black",
  101: "console-stream-ansi-bg-bright-red",
  102: "console-stream-ansi-bg-bright-green",
  103: "console-stream-ansi-bg-bright-yellow",
  104: "console-stream-ansi-bg-bright-blue",
  105: "console-stream-ansi-bg-bright-magenta",
  106: "console-stream-ansi-bg-bright-cyan",
  107: "console-stream-ansi-bg-bright-white"
};
let ae;
function sn(n) {
  ae = n, n.navigation.add({
    id: "console",
    label: "Console",
    path: "/diagnostics/console",
    activePathPrefix: "/diagnostics/console",
    parentId: "diagnostics",
    order: 840,
    iconColor: "#10b981"
  }), n.routes.add({
    id: "console",
    label: "Console",
    path: "/diagnostics/console",
    component: Nt
  }), n.panels.add({
    id: "console-stream",
    title: "Console",
    order: 1e3,
    component: At
  });
}
function At() {
  return /* @__PURE__ */ f.jsx(Ne, {});
}
function Nt() {
  return /* @__PURE__ */ f.jsx(Ne, { page: !0 });
}
function Ne({ page: n = !1 }) {
  const [e, t] = I(xt), [o, s] = I([]), [r, c] = I(""), [a, l] = I("loading"), [u, g] = I([]), [k, A] = I(0), [de, L] = I(!1), [P, ge] = I(!1), [Le, z] = I(null), [He, Q] = I(0), [N, We] = I(Vt), J = me(/* @__PURE__ */ new Set()), fe = me(null), H = P ? Le ?? u : u, Ue = P ? Math.max(0, k - He) : 0, E = oe(() => Yt(e), [e]), ee = oe(
    () => o.find((h) => h.id === r) ?? null,
    [r, o]
  ), X = r || null, j = se((h) => {
    const C = [];
    for (const b of h)
      J.current.has(b.id) || (J.current.add(b.id), C.push(b));
    C.length !== 0 && (A((b) => b + C.length), g((b) => Lt(b, C)));
  }, []), x = se((h, C) => {
    j([Ht(h, C)]);
  }, [j]), te = se(async () => {
    const h = await E.context.http.getJson(Zt(E, X));
    h.sources && s(h.sources);
    const C = h.items ?? h.lines ?? [];
    j(C.map(Te));
  }, [j, X, E]);
  V(() => {
    let h = !1;
    l("loading"), c("");
    async function C() {
      try {
        const b = await E.context.http.getJson(`${E.endpointPrefix}/sources`);
        h || (s(b), l("ready"));
      } catch {
        h || (s([]), l("failed"));
      }
    }
    return C(), () => {
      h = !0;
    };
  }, [E]), V(() => {
    window.localStorage.setItem(Ae, String(N));
  }, [N]), V(() => {
    if (!N || P)
      return;
    const h = fe.current;
    h && (h.scrollTop = h.scrollHeight);
  }, [N, P, H]), V(() => {
    let h = !1, C = null;
    J.current.clear(), g([]), A(0), z(null), Q(0);
    const b = new Tt().withUrl(Me(E, `${E.endpointPrefix}/hub`)).withAutomaticReconnect().build();
    b.serverTimeoutInMilliseconds = Rt, b.keepAliveIntervalInMilliseconds = $t, b.onreconnecting(() => L(!1)), b.onreconnected(() => {
      L(!0), ne();
    }), b.onclose(() => L(!1));
    function _e() {
      C?.dispose(), C = null;
    }
    function ne() {
      h || b.state !== p.Connected || (_e(), C = b.stream("StreamAsync", Wt(X)).subscribe({
        next: (v) => {
          if (v?.line)
            j([Te(v.line)]);
          else if (v?.droppedLines || v?.dropped) {
            const W = v.droppedLines ?? v.dropped;
            x("stderr", `${W?.count ?? 0} console lines were dropped.`);
          } else v?.source && s((W) => en(W, v.source));
        },
        error: (v) => {
          if (!h) {
            if (Kt(v)) {
              ne();
              return;
            }
            x("stderr", `Console stream failed: ${B(v)}`);
          }
        },
        complete: () => x("stdout", "Console stream completed.")
      }));
    }
    async function qe() {
      try {
        if (await b.start(), h)
          return;
        L(!0), ne(), await te();
      } catch (v) {
        if (L(!1), Gt(v)) {
          x("stderr", Qt(E));
          return;
        }
        x("stderr", `Console stream connection failed: ${B(v)}`), await te().catch((W) => x("stderr", `Recent console lines failed: ${B(W)}`));
      }
    }
    return qe(), () => {
      h = !0, _e(), b.stop();
    };
  }, [j, x, te, X, E]);
  const pe = oe(
    () => H.map((h) => ({ ...h, renderedText: Ft(h.text) })),
    [H]
  );
  function Be() {
    if (P) {
      ge(!1), z(null);
      return;
    }
    z(u), Q(k), ge(!0);
  }
  function Oe() {
    g([]), A(0), z(P ? [] : null), Q(0), J.current.clear();
  }
  function Fe() {
    qt(H);
  }
  return /* @__PURE__ */ f.jsxs("section", { className: n ? "console-stream-page" : "console-stream-panel", children: [
    /* @__PURE__ */ f.jsxs("header", { className: "console-stream-header", children: [
      /* @__PURE__ */ f.jsxs("div", { children: [
        /* @__PURE__ */ f.jsxs("h2", { children: [
          E.label,
          " console"
        ] }),
        /* @__PURE__ */ f.jsx("p", { children: tn(ee, a, Ue) })
      ] }),
      /* @__PURE__ */ f.jsxs("div", { className: "console-stream-tools", children: [
        /* @__PURE__ */ f.jsx("div", { className: "console-stream-hosts", role: "group", "aria-label": "Console host", children: Object.values(De).map((h) => /* @__PURE__ */ f.jsx(
          "button",
          {
            type: "button",
            className: h.id === e ? "active" : "",
            "aria-pressed": h.id === e,
            onClick: () => t(h.id),
            children: h.label
          },
          h.id
        )) }),
        /* @__PURE__ */ f.jsxs(
          "select",
          {
            "aria-label": "Console source",
            value: r,
            onChange: (h) => c(h.target.value),
            children: [
              /* @__PURE__ */ f.jsx("option", { value: "", children: "All sources" }),
              o.map((h) => /* @__PURE__ */ f.jsx("option", { value: h.id, children: q(h) }, h.id))
            ]
          }
        ),
        /* @__PURE__ */ f.jsx("span", { className: de ? "console-stream-status online" : "console-stream-status" }),
        /* @__PURE__ */ f.jsx("span", { children: de ? "live" : "waiting" }),
        /* @__PURE__ */ f.jsx("span", { children: "stdout" }),
        /* @__PURE__ */ f.jsx("span", { children: "stderr" }),
        /* @__PURE__ */ f.jsx("button", { type: "button", className: P ? "active" : "", onClick: Be, "aria-pressed": P, children: P ? "Resume" : "Pause" }),
        /* @__PURE__ */ f.jsx("button", { type: "button", className: N ? "active" : "", onClick: () => We((h) => !h), "aria-pressed": N, children: "Autoscroll" }),
        /* @__PURE__ */ f.jsx("button", { type: "button", onClick: Fe, disabled: H.length === 0, children: "Download" }),
        /* @__PURE__ */ f.jsx("button", { type: "button", onClick: Oe, children: "Clear" })
      ] })
    ] }),
    /* @__PURE__ */ f.jsxs("div", { className: "console-stream-lines", ref: fe, children: [
      pe.length === 0 ? /* @__PURE__ */ f.jsxs("div", { className: "console-stream-line stdout", children: [
        /* @__PURE__ */ f.jsx("span", { children: (/* @__PURE__ */ new Date()).toLocaleTimeString() }),
        /* @__PURE__ */ f.jsx("small", { children: ee ? q(ee) : "All sources" }),
        /* @__PURE__ */ f.jsx("code", { children: "Console stream is ready." })
      ] }) : null,
      pe.map((h) => /* @__PURE__ */ f.jsxs("div", { className: `console-stream-line ${h.stream}`, children: [
        /* @__PURE__ */ f.jsx("span", { children: new Date(h.timestamp).toLocaleTimeString() }),
        /* @__PURE__ */ f.jsx("small", { children: h.sourceLabel ?? "local" }),
        /* @__PURE__ */ f.jsx("code", { children: h.renderedText })
      ] }, h.id))
    ] })
  ] });
}
function jt(n) {
  return n === 1 || n === "stderr" || n === "Stderr" ? "stderr" : "stdout";
}
function Mt(n, e) {
  const t = Date.parse(n.timestamp) - Date.parse(e.timestamp);
  return t !== 0 ? t : (n.sequence ?? 0) - (e.sequence ?? 0);
}
function Lt(n, e) {
  return [...n, ...e].sort(Mt).slice(-2e3);
}
function Ht(n, e) {
  return {
    id: `${Date.now()}-${Math.random()}`,
    timestamp: (/* @__PURE__ */ new Date()).toISOString(),
    sequence: null,
    stream: n,
    text: e,
    sourceId: null,
    sourceLabel: null
  };
}
function Te(n) {
  return {
    id: n.id ?? `${n.sequence ?? Date.now()}-${Math.random()}`,
    timestamp: n.timestamp ?? n.receivedAt ?? (/* @__PURE__ */ new Date()).toISOString(),
    sequence: n.sequence ?? null,
    stream: jt(n.stream),
    text: n.text ?? "",
    sourceId: n.source?.id ?? null,
    sourceLabel: n.source ? q(n.source) : null
  };
}
function q(n) {
  if (n.podName)
    return [n.namespace, n.podName, n.containerName].filter(Boolean).join(" / ");
  const e = n.displayName || n.serviceName || n.id;
  return n.machineName && n.processId ? `${e} · ${n.machineName}:${n.processId}` : e;
}
function Wt(n) {
  return n ? { limit: ce, sourceId: n } : { limit: ce };
}
function Ut(n) {
  return n.map(zt).join(`
`);
}
function Bt(n = /* @__PURE__ */ new Date()) {
  return `console-log-${n.toISOString().replace(/\.\d{3}Z$/, "Z").replace(/[:.]/g, "-")}.log`;
}
function Ot(n) {
  if (!n.includes("\x1B["))
    return [{ text: n, className: "" }];
  const e = [], t = je();
  let o = 0, s;
  for (G.lastIndex = 0; (s = G.exec(n)) !== null; ) {
    s.index > o && e.push({ text: n.slice(o, s.index), className: Pe(t) });
    const r = s[1] === "" ? [0] : s[1].split(";").map((c) => Number(c || 0));
    Xt(t, r), o = G.lastIndex;
  }
  return o < n.length && e.push({ text: n.slice(o), className: Pe(t) }), e;
}
function Ft(n) {
  return Ot(n).map((e, t) => e.className ? /* @__PURE__ */ f.jsx("span", { className: e.className, children: e.text }, t) : e.text);
}
function qt(n) {
  if (n.length === 0 || typeof document > "u")
    return;
  const e = new Blob([Ut(n)], { type: "text/plain;charset=utf-8" }), t = URL.createObjectURL(e), o = document.createElement("a");
  o.href = t, o.download = Bt(), o.rel = "noopener", document.body.appendChild(o), o.click(), o.remove(), window.setTimeout(() => URL.revokeObjectURL(t), 0);
}
function zt(n) {
  const e = n.sourceLabel ?? n.sourceId ?? "local";
  return `${n.timestamp} ${n.stream} ${e} ${Jt(n.text)}`;
}
function Jt(n) {
  return n.replace(G, "");
}
function je() {
  return {
    bold: !1,
    dim: !1,
    foreground: "",
    background: ""
  };
}
function Xt(n, e) {
  for (const t of e)
    t === 0 ? Object.assign(n, je()) : t === 1 ? (n.bold = !0, n.dim = !1) : t === 2 ? (n.dim = !0, n.bold = !1) : t === 22 ? (n.bold = !1, n.dim = !1) : t === 39 ? n.foreground = "" : t === 49 ? n.background = "" : Ee[t] ? n.foreground = Ee[t] : Ie[t] && (n.background = Ie[t]);
}
function Pe(n) {
  return [
    n.bold ? "console-stream-ansi-bold" : "",
    n.dim ? "console-stream-ansi-dim" : "",
    n.foreground,
    n.background
  ].filter(Boolean).join(" ");
}
function Vt() {
  return typeof window > "u" ? !0 : window.localStorage.getItem(Ae) !== "false";
}
function B(n) {
  return n instanceof Error ? n.message : String(n);
}
function Kt(n) {
  return B(n) === Dt;
}
function Gt(n) {
  return /\b404\b/.test(B(n));
}
function Yt(n) {
  return {
    ...De[n],
    context: n === "server" ? ae.backend : ae.host
  };
}
function Zt(n, e) {
  const t = new URLSearchParams({ limit: String(ce) });
  return e && t.set("sourceId", e), `${n.endpointPrefix}/recent?${t}`;
}
function Me(n, e) {
  return new URL(e, n.context.baseUrl).toString();
}
function Qt(n) {
  return `Console stream endpoint was not found at ${Me(n, n.endpointPrefix)}. Make sure ${n.label} maps console streaming or choose another host.`;
}
function en(n, e) {
  const t = n.findIndex((s) => s.id === e.id);
  if (t === -1)
    return [...n, e].sort(xe);
  const o = [...n];
  return o[t] = e, o.sort(xe);
}
function xe(n, e) {
  return q(n).localeCompare(q(e));
}
function tn(n, e, t) {
  return t > 0 ? `${t} buffered while paused` : n ? n.podName ? [n.namespace, n.nodeName].filter(Boolean).join(" / ") : n.id : e === "loading" ? "Loading sources" : e === "failed" ? "Sources unavailable" : "Merged source stream";
}
export {
  Nt as ConsoleStreamPage,
  At as ConsoleStreamPanel,
  Lt as appendConsoleEntries,
  Mt as compareConsoleEntries,
  Ht as createConsoleEntry,
  Te as createConsoleEntryFromLine,
  Ut as createConsoleExportContent,
  Bt as createConsoleExportFilename,
  Wt as createConsoleFilter,
  q as formatConsoleSourceLabel,
  jt as getConsoleStreamName,
  Gt as isConsoleStreamEndpointNotFoundError,
  Kt as isRecoverableConsoleStreamError,
  Ot as parseAnsiSegments,
  sn as register
};
