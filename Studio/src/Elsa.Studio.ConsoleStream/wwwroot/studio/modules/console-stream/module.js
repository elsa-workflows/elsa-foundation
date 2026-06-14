import { useState as T, useRef as ne, useCallback as F, useEffect as z, useMemo as Ee } from "react";
var J = { exports: {} }, N = {};
var oe;
function Ie() {
  if (oe) return N;
  oe = 1;
  var o = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function t(n, s, r) {
    var c = null;
    if (r !== void 0 && (c = "" + r), s.key !== void 0 && (c = "" + s.key), "key" in s) {
      r = {};
      for (var a in s)
        a !== "key" && (r[a] = s[a]);
    } else r = s;
    return s = r.ref, {
      $$typeof: o,
      type: n,
      key: c,
      ref: s !== void 0 ? s : null,
      props: r
    };
  }
  return N.Fragment = e, N.jsx = t, N.jsxs = t, N;
}
var se;
function Te() {
  return se || (se = 1, J.exports = Ie()), J.exports;
}
var p = Te();
class P extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.HttpError}.
   *
   * @param {string} errorMessage A descriptive error message.
   * @param {number} statusCode The HTTP status code represented by this error.
   */
  constructor(e, t) {
    const n = new.target.prototype;
    super(`${e}: Status code '${t}'`), this.statusCode = t, this.__proto__ = n;
  }
}
class G extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.TimeoutError}.
   *
   * @param {string} errorMessage A descriptive error message.
   */
  constructor(e = "A timeout occurred.") {
    const t = new.target.prototype;
    super(e), this.__proto__ = t;
  }
}
class k extends Error {
  /** Constructs a new instance of {@link AbortError}.
   *
   * @param {string} errorMessage A descriptive error message.
   */
  constructor(e = "An abort occurred.") {
    const t = new.target.prototype;
    super(e), this.__proto__ = t;
  }
}
class Pe extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.UnsupportedTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const n = new.target.prototype;
    super(e), this.transport = t, this.errorType = "UnsupportedTransportError", this.__proto__ = n;
  }
}
class Re extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.DisabledTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const n = new.target.prototype;
    super(e), this.transport = t, this.errorType = "DisabledTransportError", this.__proto__ = n;
  }
}
class $e extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.FailedToStartTransportError}.
   *
   * @param {string} message A descriptive error message.
   * @param {HttpTransportType} transport The {@link @microsoft/signalr.HttpTransportType} this error occurred on.
   */
  constructor(e, t) {
    const n = new.target.prototype;
    super(e), this.transport = t, this.errorType = "FailedToStartTransportError", this.__proto__ = n;
  }
}
class re extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.FailedToNegotiateWithServerError}.
   *
   * @param {string} message A descriptive error message.
   */
  constructor(e) {
    const t = new.target.prototype;
    super(e), this.errorType = "FailedToNegotiateWithServerError", this.__proto__ = t;
  }
}
class xe extends Error {
  /** Constructs a new instance of {@link @microsoft/signalr.AggregateErrors}.
   *
   * @param {string} message A descriptive error message.
   * @param {Error[]} innerErrors The collection of errors this error is aggregating.
   */
  constructor(e, t) {
    const n = new.target.prototype;
    super(e), this.innerErrors = t, this.__proto__ = n;
  }
}
class _e {
  constructor(e, t, n) {
    this.statusCode = e, this.statusText = t, this.content = n;
  }
}
class W {
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
(function(o) {
  o[o.Trace = 0] = "Trace", o[o.Debug = 1] = "Debug", o[o.Information = 2] = "Information", o[o.Warning = 3] = "Warning", o[o.Error = 4] = "Error", o[o.Critical = 5] = "Critical", o[o.None = 6] = "None";
})(i || (i = {}));
class H {
  constructor() {
  }
  /** @inheritDoc */
  // eslint-disable-next-line
  log(e, t) {
  }
}
H.instance = new H();
const De = "8.0.17";
class m {
  static isRequired(e, t) {
    if (e == null)
      throw new Error(`The '${t}' argument is required.`);
  }
  static isNotEmpty(e, t) {
    if (!e || e.match(/^\s*$/))
      throw new Error(`The '${t}' argument should not be empty.`);
  }
  static isIn(e, t, n) {
    if (!(e in t))
      throw new Error(`Unknown ${n} value: ${e}.`);
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
function M(o, e) {
  let t = "";
  return $(o) ? (t = `Binary data of length ${o.byteLength}`, e && (t += `. Content: '${Ae(o)}'`)) : typeof o == "string" && (t = `String data of length ${o.length}`, e && (t += `. Content: '${o}'`)), t;
}
function Ae(o) {
  const e = new Uint8Array(o);
  let t = "";
  return e.forEach((n) => {
    const s = n < 16 ? "0" : "";
    t += `0x${s}${n.toString(16)} `;
  }), t.substr(0, t.length - 1);
}
function $(o) {
  return o && typeof ArrayBuffer < "u" && (o instanceof ArrayBuffer || // Sometimes we get an ArrayBuffer that doesn't satisfy instanceof
  o.constructor && o.constructor.name === "ArrayBuffer");
}
async function pe(o, e, t, n, s, r) {
  const c = {}, [a, l] = A();
  c[a] = l, o.log(i.Trace, `(${e} transport) sending data. ${M(s, r.logMessageContent)}.`);
  const h = $(s) ? "arraybuffer" : "text", d = await t.post(n, {
    content: s,
    headers: { ...c, ...r.headers },
    responseType: h,
    timeout: r.timeout,
    withCredentials: r.withCredentials
  });
  o.log(i.Trace, `(${e} transport) request complete. Response status: ${d.statusCode}.`);
}
function Ne(o) {
  return o === void 0 ? new L(i.Information) : o === null ? H.instance : o.log !== void 0 ? o : new L(o);
}
class He {
  constructor(e, t) {
    this._subject = e, this._observer = t;
  }
  dispose() {
    const e = this._subject.observers.indexOf(this._observer);
    e > -1 && this._subject.observers.splice(e, 1), this._subject.observers.length === 0 && this._subject.cancelCallback && this._subject.cancelCallback().catch((t) => {
    });
  }
}
class L {
  constructor(e) {
    this._minLevel = e, this.out = console;
  }
  log(e, t) {
    if (e >= this._minLevel) {
      const n = `[${(/* @__PURE__ */ new Date()).toISOString()}] ${i[e]}: ${t}`;
      switch (e) {
        case i.Critical:
        case i.Error:
          this.out.error(n);
          break;
        case i.Warning:
          this.out.warn(n);
          break;
        case i.Information:
          this.out.info(n);
          break;
        default:
          this.out.log(n);
          break;
      }
    }
  }
}
function A() {
  let o = "X-SignalR-User-Agent";
  return _.isNode && (o = "User-Agent"), [o, Me(De, je(), We(), Le())];
}
function Me(o, e, t, n) {
  let s = "Microsoft SignalR/";
  const r = o.split(".");
  return s += `${r[0]}.${r[1]}`, s += ` (${o}; `, e && e !== "" ? s += `${e}; ` : s += "Unknown OS; ", s += `${t}`, n ? s += `; ${n}` : s += "; Unknown Runtime Version", s += ")", s;
}
function je() {
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
function Le() {
  if (_.isNode)
    return process.versions.node;
}
function We() {
  return _.isNode ? "NodeJS" : "Browser";
}
function X(o) {
  return o.stack ? o.stack : o.message ? o.message : `${o}`;
}
function Be() {
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
class Ue extends W {
  constructor(e) {
    if (super(), this._logger = e, typeof fetch > "u" || _.isNode) {
      const t = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      this._jar = new (t("tough-cookie")).CookieJar(), typeof fetch > "u" ? this._fetchType = t("node-fetch") : this._fetchType = fetch, this._fetchType = t("fetch-cookie")(this._fetchType, this._jar);
    } else
      this._fetchType = fetch.bind(Be());
    if (typeof AbortController > "u") {
      const t = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      this._abortControllerType = t("abort-controller");
    } else
      this._abortControllerType = AbortController;
  }
  /** @inheritDoc */
  async send(e) {
    if (e.abortSignal && e.abortSignal.aborted)
      throw new k();
    if (!e.method)
      throw new Error("No method defined.");
    if (!e.url)
      throw new Error("No url defined.");
    const t = new this._abortControllerType();
    let n;
    e.abortSignal && (e.abortSignal.onabort = () => {
      t.abort(), n = new k();
    });
    let s = null;
    if (e.timeout) {
      const l = e.timeout;
      s = setTimeout(() => {
        t.abort(), this._logger.log(i.Warning, "Timeout from HTTP request."), n = new G();
      }, l);
    }
    e.content === "" && (e.content = void 0), e.content && (e.headers = e.headers || {}, $(e.content) ? e.headers["Content-Type"] = "application/octet-stream" : e.headers["Content-Type"] = "text/plain;charset=UTF-8");
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
      throw n || (this._logger.log(i.Warning, `Error from HTTP request. ${l}.`), l);
    } finally {
      s && clearTimeout(s), e.abortSignal && (e.abortSignal.onabort = null);
    }
    if (!r.ok) {
      const l = await ie(r, "text");
      throw new P(l || r.statusText, r.status);
    }
    const a = await ie(r, e.responseType);
    return new _e(r.status, r.statusText, a);
  }
  getCookieString(e) {
    let t = "";
    return _.isNode && this._jar && this._jar.getCookies(e, (n, s) => t = s.join("; ")), t;
  }
}
function ie(o, e) {
  let t;
  switch (e) {
    case "arraybuffer":
      t = o.arrayBuffer();
      break;
    case "text":
      t = o.text();
      break;
    case "blob":
    case "document":
    case "json":
      throw new Error(`${e} is not supported.`);
    default:
      t = o.text();
      break;
  }
  return t;
}
class Oe extends W {
  constructor(e) {
    super(), this._logger = e;
  }
  /** @inheritDoc */
  send(e) {
    return e.abortSignal && e.abortSignal.aborted ? Promise.reject(new k()) : e.method ? e.url ? new Promise((t, n) => {
      const s = new XMLHttpRequest();
      s.open(e.method, e.url, !0), s.withCredentials = e.withCredentials === void 0 ? !0 : e.withCredentials, s.setRequestHeader("X-Requested-With", "XMLHttpRequest"), e.content === "" && (e.content = void 0), e.content && ($(e.content) ? s.setRequestHeader("Content-Type", "application/octet-stream") : s.setRequestHeader("Content-Type", "text/plain;charset=UTF-8"));
      const r = e.headers;
      r && Object.keys(r).forEach((c) => {
        s.setRequestHeader(c, r[c]);
      }), e.responseType && (s.responseType = e.responseType), e.abortSignal && (e.abortSignal.onabort = () => {
        s.abort(), n(new k());
      }), e.timeout && (s.timeout = e.timeout), s.onload = () => {
        e.abortSignal && (e.abortSignal.onabort = null), s.status >= 200 && s.status < 300 ? t(new _e(s.status, s.statusText, s.response || s.responseText)) : n(new P(s.response || s.responseText || s.statusText, s.status));
      }, s.onerror = () => {
        this._logger.log(i.Warning, `Error from HTTP request. ${s.status}: ${s.statusText}.`), n(new P(s.statusText, s.status));
      }, s.ontimeout = () => {
        this._logger.log(i.Warning, "Timeout from HTTP request."), n(new G());
      }, s.send(e.content);
    }) : Promise.reject(new Error("No url defined.")) : Promise.reject(new Error("No method defined."));
  }
}
class qe extends W {
  /** Creates a new instance of the {@link @microsoft/signalr.DefaultHttpClient}, using the provided {@link @microsoft/signalr.ILogger} to log messages. */
  constructor(e) {
    if (super(), typeof fetch < "u" || _.isNode)
      this._httpClient = new Ue(e);
    else if (typeof XMLHttpRequest < "u")
      this._httpClient = new Oe(e);
    else
      throw new Error("No usable HttpClient found.");
  }
  /** @inheritDoc */
  send(e) {
    return e.abortSignal && e.abortSignal.aborted ? Promise.reject(new k()) : e.method ? e.url ? this._httpClient.send(e) : Promise.reject(new Error("No url defined.")) : Promise.reject(new Error("No method defined."));
  }
  getCookieString(e) {
    return this._httpClient.getCookieString(e);
  }
}
class C {
  static write(e) {
    return `${e}${C.RecordSeparator}`;
  }
  static parse(e) {
    if (e[e.length - 1] !== C.RecordSeparator)
      throw new Error("Message is incomplete.");
    const t = e.split(C.RecordSeparator);
    return t.pop(), t;
  }
}
C.RecordSeparatorCode = 30;
C.RecordSeparator = String.fromCharCode(C.RecordSeparatorCode);
class Fe {
  // Handshake request is always JSON
  writeHandshakeRequest(e) {
    return C.write(JSON.stringify(e));
  }
  parseHandshakeResponse(e) {
    let t, n;
    if ($(e)) {
      const a = new Uint8Array(e), l = a.indexOf(C.RecordSeparatorCode);
      if (l === -1)
        throw new Error("Message is incomplete.");
      const h = l + 1;
      t = String.fromCharCode.apply(null, Array.prototype.slice.call(a.slice(0, h))), n = a.byteLength > h ? a.slice(h).buffer : null;
    } else {
      const a = e, l = a.indexOf(C.RecordSeparator);
      if (l === -1)
        throw new Error("Message is incomplete.");
      const h = l + 1;
      t = a.substring(0, h), n = a.length > h ? a.substring(h) : null;
    }
    const s = C.parse(t), r = JSON.parse(s[0]);
    if (r.type)
      throw new Error("Expected a handshake response from the server.");
    return [n, r];
  }
}
var u;
(function(o) {
  o[o.Invocation = 1] = "Invocation", o[o.StreamItem = 2] = "StreamItem", o[o.Completion = 3] = "Completion", o[o.StreamInvocation = 4] = "StreamInvocation", o[o.CancelInvocation = 5] = "CancelInvocation", o[o.Ping = 6] = "Ping", o[o.Close = 7] = "Close", o[o.Ack = 8] = "Ack", o[o.Sequence = 9] = "Sequence";
})(u || (u = {}));
class ze {
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
    return this.observers.push(e), new He(this, e);
  }
}
class Je {
  constructor(e, t, n) {
    this._bufferSize = 1e5, this._messages = [], this._totalMessageCount = 0, this._waitForSequenceMessage = !1, this._nextReceivingSequenceId = 1, this._latestReceivedSequenceId = 0, this._bufferedByteCount = 0, this._reconnectInProgress = !1, this._protocol = e, this._connection = t, this._bufferSize = n;
  }
  async _send(e) {
    const t = this._protocol.writeMessage(e);
    let n = Promise.resolve();
    if (this._isInvocationMessage(e)) {
      this._totalMessageCount++;
      let s = () => {
      }, r = () => {
      };
      $(t) ? this._bufferedByteCount += t.byteLength : this._bufferedByteCount += t.length, this._bufferedByteCount >= this._bufferSize && (n = new Promise((c, a) => {
        s = c, r = a;
      })), this._messages.push(new Xe(t, this._totalMessageCount, s, r));
    }
    try {
      this._reconnectInProgress || await this._connection.send(t);
    } catch {
      this._disconnected();
    }
    await n;
  }
  _ack(e) {
    let t = -1;
    for (let n = 0; n < this._messages.length; n++) {
      const s = this._messages[n];
      if (s._id <= e.sequenceId)
        t = n, $(s._message) ? this._bufferedByteCount -= s._message.byteLength : this._bufferedByteCount -= s._message.length, s._resolver();
      else if (this._bufferedByteCount < this._bufferSize)
        s._resolver();
      else
        break;
    }
    t !== -1 && (this._messages = this._messages.slice(t + 1));
  }
  _shouldProcessMessage(e) {
    if (this._waitForSequenceMessage)
      return e.type !== u.Sequence ? !1 : (this._waitForSequenceMessage = !1, !0);
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
    await this._connection.send(this._protocol.writeMessage({ type: u.Sequence, sequenceId: e }));
    const t = this._messages;
    for (const n of t)
      await this._connection.send(n._message);
    this._reconnectInProgress = !1;
  }
  _dispose(e) {
    e ?? (e = new Error("Unable to reconnect to server."));
    for (const t of this._messages)
      t._rejector(e);
  }
  _isInvocationMessage(e) {
    switch (e.type) {
      case u.Invocation:
      case u.StreamItem:
      case u.Completion:
      case u.StreamInvocation:
      case u.CancelInvocation:
        return !0;
      case u.Close:
      case u.Sequence:
      case u.Ping:
      case u.Ack:
        return !1;
    }
  }
  _ackTimer() {
    this._ackTimerHandle === void 0 && (this._ackTimerHandle = setTimeout(async () => {
      try {
        this._reconnectInProgress || await this._connection.send(this._protocol.writeMessage({ type: u.Ack, sequenceId: this._latestReceivedSequenceId }));
      } catch {
      }
      clearTimeout(this._ackTimerHandle), this._ackTimerHandle = void 0;
    }, 1e3));
  }
}
class Xe {
  constructor(e, t, n, s) {
    this._message = e, this._id = t, this._resolver = n, this._rejector = s;
  }
}
const Ve = 30 * 1e3, Ke = 15 * 1e3, Ge = 1e5;
var g;
(function(o) {
  o.Disconnected = "Disconnected", o.Connecting = "Connecting", o.Connected = "Connected", o.Disconnecting = "Disconnecting", o.Reconnecting = "Reconnecting";
})(g || (g = {}));
class Y {
  /** @internal */
  // Using a public static factory method means we can have a private constructor and an _internal_
  // create method that can be used by HubConnectionBuilder. An "internal" constructor would just
  // be stripped away and the '.d.ts' file would have no constructor, which is interpreted as a
  // public parameter-less constructor.
  static create(e, t, n, s, r, c, a) {
    return new Y(e, t, n, s, r, c, a);
  }
  constructor(e, t, n, s, r, c, a) {
    this._nextKeepAlive = 0, this._freezeEventListener = () => {
      this._logger.log(i.Warning, "The page is being frozen, this will likely lead to the connection being closed and messages being lost. For more information see the docs at https://learn.microsoft.com/aspnet/core/signalr/javascript-client#bsleep");
    }, m.isRequired(e, "connection"), m.isRequired(t, "logger"), m.isRequired(n, "protocol"), this.serverTimeoutInMilliseconds = r ?? Ve, this.keepAliveIntervalInMilliseconds = c ?? Ke, this._statefulReconnectBufferSize = a ?? Ge, this._logger = t, this._protocol = n, this.connection = e, this._reconnectPolicy = s, this._handshakeProtocol = new Fe(), this.connection.onreceive = (l) => this._processIncomingData(l), this.connection.onclose = (l) => this._connectionClosed(l), this._callbacks = {}, this._methods = {}, this._closedCallbacks = [], this._reconnectingCallbacks = [], this._reconnectedCallbacks = [], this._invocationId = 0, this._receivedHandshakeResponse = !1, this._connectionState = g.Disconnected, this._connectionStarted = !1, this._cachedPingMessage = this._protocol.writeMessage({ type: u.Ping });
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
    if (this._connectionState !== g.Disconnected && this._connectionState !== g.Reconnecting)
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
    if (this._connectionState !== g.Disconnected)
      return Promise.reject(new Error("Cannot start a HubConnection that is not in the 'Disconnected' state."));
    this._connectionState = g.Connecting, this._logger.log(i.Debug, "Starting HubConnection.");
    try {
      await this._startInternal(), _.isBrowser && window.document.addEventListener("freeze", this._freezeEventListener), this._connectionState = g.Connected, this._connectionStarted = !0, this._logger.log(i.Debug, "HubConnection connected successfully.");
    } catch (e) {
      return this._connectionState = g.Disconnected, this._logger.log(i.Debug, `HubConnection failed to start successfully because of error '${e}'.`), Promise.reject(e);
    }
  }
  async _startInternal() {
    this._stopDuringStartError = void 0, this._receivedHandshakeResponse = !1;
    const e = new Promise((t, n) => {
      this._handshakeResolver = t, this._handshakeRejecter = n;
    });
    await this.connection.start(this._protocol.transferFormat);
    try {
      let t = this._protocol.version;
      this.connection.features.reconnect || (t = 1);
      const n = {
        protocol: this._protocol.name,
        version: t
      };
      if (this._logger.log(i.Debug, "Sending handshake request."), await this._sendMessage(this._handshakeProtocol.writeHandshakeRequest(n)), this._logger.log(i.Information, `Using HubProtocol '${this._protocol.name}'.`), this._cleanupTimeout(), this._resetTimeoutPeriod(), this._resetKeepAliveInterval(), await e, this._stopDuringStartError)
        throw this._stopDuringStartError;
      this.connection.features.reconnect && (this._messageBuffer = new Je(this._protocol, this.connection, this._statefulReconnectBufferSize), this.connection.features.disconnected = this._messageBuffer._disconnected.bind(this._messageBuffer), this.connection.features.resend = () => {
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
    if (this._connectionState === g.Disconnected)
      return this._logger.log(i.Debug, `Call to HubConnection.stop(${e}) ignored because it is already in the disconnected state.`), Promise.resolve();
    if (this._connectionState === g.Disconnecting)
      return this._logger.log(i.Debug, `Call to HttpConnection.stop(${e}) ignored because the connection is already in the disconnecting state.`), this._stopPromise;
    const t = this._connectionState;
    return this._connectionState = g.Disconnecting, this._logger.log(i.Debug, "Stopping HubConnection."), this._reconnectDelayHandle ? (this._logger.log(i.Debug, "Connection stopped during reconnect delay. Done reconnecting."), clearTimeout(this._reconnectDelayHandle), this._reconnectDelayHandle = void 0, this._completeClose(), Promise.resolve()) : (t === g.Connected && this._sendCloseMessage(), this._cleanupTimeout(), this._cleanupPingTimer(), this._stopDuringStartError = e || new k("The connection was stopped before the hub handshake could complete."), this.connection.stop(e));
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
    const [n, s] = this._replaceStreamingParams(t), r = this._createStreamInvocation(e, t, s);
    let c;
    const a = new ze();
    return a.cancelCallback = () => {
      const l = this._createCancelInvocation(r.invocationId);
      return delete this._callbacks[r.invocationId], c.then(() => this._sendWithProtocol(l));
    }, this._callbacks[r.invocationId] = (l, h) => {
      if (h) {
        a.error(h);
        return;
      } else l && (l.type === u.Completion ? l.error ? a.error(new Error(l.error)) : a.complete() : a.next(l.item));
    }, c = this._sendWithProtocol(r).catch((l) => {
      a.error(l), delete this._callbacks[r.invocationId];
    }), this._launchStreams(n, c), a;
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
    const [n, s] = this._replaceStreamingParams(t), r = this._sendWithProtocol(this._createInvocation(e, t, !0, s));
    return this._launchStreams(n, r), r;
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
    const [n, s] = this._replaceStreamingParams(t), r = this._createInvocation(e, t, !1, s);
    return new Promise((a, l) => {
      this._callbacks[r.invocationId] = (d, y) => {
        if (y) {
          l(y);
          return;
        } else d && (d.type === u.Completion ? d.error ? l(new Error(d.error)) : a(d.result) : l(new Error(`Unexpected message type: ${d.type}`)));
      };
      const h = this._sendWithProtocol(r).catch((d) => {
        l(d), delete this._callbacks[r.invocationId];
      });
      this._launchStreams(n, h);
    });
  }
  on(e, t) {
    !e || !t || (e = e.toLowerCase(), this._methods[e] || (this._methods[e] = []), this._methods[e].indexOf(t) === -1 && this._methods[e].push(t));
  }
  off(e, t) {
    if (!e)
      return;
    e = e.toLowerCase();
    const n = this._methods[e];
    if (n)
      if (t) {
        const s = n.indexOf(t);
        s !== -1 && (n.splice(s, 1), n.length === 0 && delete this._methods[e]);
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
      for (const n of t)
        if (!(this._messageBuffer && !this._messageBuffer._shouldProcessMessage(n)))
          switch (n.type) {
            case u.Invocation:
              this._invokeClientMethod(n).catch((s) => {
                this._logger.log(i.Error, `Invoke client method threw error: ${X(s)}`);
              });
              break;
            case u.StreamItem:
            case u.Completion: {
              const s = this._callbacks[n.invocationId];
              if (s) {
                n.type === u.Completion && delete this._callbacks[n.invocationId];
                try {
                  s(n);
                } catch (r) {
                  this._logger.log(i.Error, `Stream callback threw error: ${X(r)}`);
                }
              }
              break;
            }
            case u.Ping:
              break;
            case u.Close: {
              this._logger.log(i.Information, "Close message received from server.");
              const s = n.error ? new Error("Server returned an error on close: " + n.error) : void 0;
              n.allowReconnect === !0 ? this.connection.stop(s) : this._stopPromise = this._stopInternal(s);
              break;
            }
            case u.Ack:
              this._messageBuffer && this._messageBuffer._ack(n);
              break;
            case u.Sequence:
              this._messageBuffer && this._messageBuffer._resetSequence(n);
              break;
            default:
              this._logger.log(i.Warning, `Invalid message type: ${n.type}.`);
              break;
          }
    }
    this._resetTimeoutPeriod();
  }
  _processHandshakeResponse(e) {
    let t, n;
    try {
      [n, t] = this._handshakeProtocol.parseHandshakeResponse(e);
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
    return this._handshakeResolver(), n;
  }
  _resetKeepAliveInterval() {
    this.connection.features.inherentKeepAlive || (this._nextKeepAlive = (/* @__PURE__ */ new Date()).getTime() + this.keepAliveIntervalInMilliseconds, this._cleanupPingTimer());
  }
  _resetTimeoutPeriod() {
    if ((!this.connection.features || !this.connection.features.inherentKeepAlive) && (this._timeoutHandle = setTimeout(() => this.serverTimeout(), this.serverTimeoutInMilliseconds), this._pingServerHandle === void 0)) {
      let e = this._nextKeepAlive - (/* @__PURE__ */ new Date()).getTime();
      e < 0 && (e = 0), this._pingServerHandle = setTimeout(async () => {
        if (this._connectionState === g.Connected)
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
    const t = e.target.toLowerCase(), n = this._methods[t];
    if (!n) {
      this._logger.log(i.Warning, `No client method with the name '${t}' found.`), e.invocationId && (this._logger.log(i.Warning, `No result given for '${t}' method and invocation ID '${e.invocationId}'.`), await this._sendWithProtocol(this._createCompletionMessage(e.invocationId, "Client didn't provide a result.", null)));
      return;
    }
    const s = n.slice(), r = !!e.invocationId;
    let c, a, l;
    for (const h of s)
      try {
        const d = c;
        c = await h.apply(this, e.arguments), r && c && d && (this._logger.log(i.Error, `Multiple results provided for '${t}'. Sending error to server.`), l = this._createCompletionMessage(e.invocationId, "Client provided multiple results.", null)), a = void 0;
      } catch (d) {
        a = d, this._logger.log(i.Error, `A callback for the method '${t}' threw error '${d}'.`);
      }
    l ? await this._sendWithProtocol(l) : r ? (a ? l = this._createCompletionMessage(e.invocationId, `${a}`, null) : c !== void 0 ? l = this._createCompletionMessage(e.invocationId, null, c) : (this._logger.log(i.Warning, `No result given for '${t}' method and invocation ID '${e.invocationId}'.`), l = this._createCompletionMessage(e.invocationId, "Client didn't provide a result.", null)), await this._sendWithProtocol(l)) : c && this._logger.log(i.Error, `Result given for '${t}' method but server is not expecting a result.`);
  }
  _connectionClosed(e) {
    this._logger.log(i.Debug, `HubConnection.connectionClosed(${e}) called while in state ${this._connectionState}.`), this._stopDuringStartError = this._stopDuringStartError || e || new k("The underlying connection was closed before the hub handshake could complete."), this._handshakeResolver && this._handshakeResolver(), this._cancelCallbacksWithError(e || new Error("Invocation canceled due to the underlying connection being closed.")), this._cleanupTimeout(), this._cleanupPingTimer(), this._connectionState === g.Disconnecting ? this._completeClose(e) : this._connectionState === g.Connected && this._reconnectPolicy ? this._reconnect(e) : this._connectionState === g.Connected && this._completeClose(e);
  }
  _completeClose(e) {
    if (this._connectionStarted) {
      this._connectionState = g.Disconnected, this._connectionStarted = !1, this._messageBuffer && (this._messageBuffer._dispose(e ?? new Error("Connection closed.")), this._messageBuffer = void 0), _.isBrowser && window.document.removeEventListener("freeze", this._freezeEventListener);
      try {
        this._closedCallbacks.forEach((t) => t.apply(this, [e]));
      } catch (t) {
        this._logger.log(i.Error, `An onclose callback called with error '${e}' threw error '${t}'.`);
      }
    }
  }
  async _reconnect(e) {
    const t = Date.now();
    let n = 0, s = e !== void 0 ? e : new Error("Attempting to reconnect due to a unknown error."), r = this._getNextRetryDelay(n++, 0, s);
    if (r === null) {
      this._logger.log(i.Debug, "Connection not reconnecting because the IRetryPolicy returned null on the first reconnect attempt."), this._completeClose(e);
      return;
    }
    if (this._connectionState = g.Reconnecting, e ? this._logger.log(i.Information, `Connection reconnecting because of error '${e}'.`) : this._logger.log(i.Information, "Connection reconnecting."), this._reconnectingCallbacks.length !== 0) {
      try {
        this._reconnectingCallbacks.forEach((c) => c.apply(this, [e]));
      } catch (c) {
        this._logger.log(i.Error, `An onreconnecting callback called with error '${e}' threw error '${c}'.`);
      }
      if (this._connectionState !== g.Reconnecting) {
        this._logger.log(i.Debug, "Connection left the reconnecting state in onreconnecting callback. Done reconnecting.");
        return;
      }
    }
    for (; r !== null; ) {
      if (this._logger.log(i.Information, `Reconnect attempt number ${n} will start in ${r} ms.`), await new Promise((c) => {
        this._reconnectDelayHandle = setTimeout(c, r);
      }), this._reconnectDelayHandle = void 0, this._connectionState !== g.Reconnecting) {
        this._logger.log(i.Debug, "Connection left the reconnecting state during reconnect delay. Done reconnecting.");
        return;
      }
      try {
        if (await this._startInternal(), this._connectionState = g.Connected, this._logger.log(i.Information, "HubConnection reconnected successfully."), this._reconnectedCallbacks.length !== 0)
          try {
            this._reconnectedCallbacks.forEach((c) => c.apply(this, [this.connection.connectionId]));
          } catch (c) {
            this._logger.log(i.Error, `An onreconnected callback called with connectionId '${this.connection.connectionId}; threw error '${c}'.`);
          }
        return;
      } catch (c) {
        if (this._logger.log(i.Information, `Reconnect attempt failed because of error '${c}'.`), this._connectionState !== g.Reconnecting) {
          this._logger.log(i.Debug, `Connection moved to the '${this._connectionState}' from the reconnecting state during reconnect attempt. Done reconnecting.`), this._connectionState === g.Disconnecting && this._completeClose();
          return;
        }
        s = c instanceof Error ? c : new Error(c.toString()), r = this._getNextRetryDelay(n++, Date.now() - t, s);
      }
    }
    this._logger.log(i.Information, `Reconnect retries have been exhausted after ${Date.now() - t} ms and ${n} failed attempts. Connection disconnecting.`), this._completeClose();
  }
  _getNextRetryDelay(e, t, n) {
    try {
      return this._reconnectPolicy.nextRetryDelayInMilliseconds({
        elapsedMilliseconds: t,
        previousRetryCount: e,
        retryReason: n
      });
    } catch (s) {
      return this._logger.log(i.Error, `IRetryPolicy.nextRetryDelayInMilliseconds(${e}, ${t}) threw error '${s}'.`), null;
    }
  }
  _cancelCallbacksWithError(e) {
    const t = this._callbacks;
    this._callbacks = {}, Object.keys(t).forEach((n) => {
      const s = t[n];
      try {
        s(null, e);
      } catch (r) {
        this._logger.log(i.Error, `Stream 'error' callback called with '${e}' threw error: ${X(r)}`);
      }
    });
  }
  _cleanupPingTimer() {
    this._pingServerHandle && (clearTimeout(this._pingServerHandle), this._pingServerHandle = void 0);
  }
  _cleanupTimeout() {
    this._timeoutHandle && clearTimeout(this._timeoutHandle);
  }
  _createInvocation(e, t, n, s) {
    if (n)
      return s.length !== 0 ? {
        arguments: t,
        streamIds: s,
        target: e,
        type: u.Invocation
      } : {
        arguments: t,
        target: e,
        type: u.Invocation
      };
    {
      const r = this._invocationId;
      return this._invocationId++, s.length !== 0 ? {
        arguments: t,
        invocationId: r.toString(),
        streamIds: s,
        target: e,
        type: u.Invocation
      } : {
        arguments: t,
        invocationId: r.toString(),
        target: e,
        type: u.Invocation
      };
    }
  }
  _launchStreams(e, t) {
    if (e.length !== 0) {
      t || (t = Promise.resolve());
      for (const n in e)
        e[n].subscribe({
          complete: () => {
            t = t.then(() => this._sendWithProtocol(this._createCompletionMessage(n)));
          },
          error: (s) => {
            let r;
            s instanceof Error ? r = s.message : s && s.toString ? r = s.toString() : r = "Unknown error", t = t.then(() => this._sendWithProtocol(this._createCompletionMessage(n, r)));
          },
          next: (s) => {
            t = t.then(() => this._sendWithProtocol(this._createStreamItemMessage(n, s)));
          }
        });
    }
  }
  _replaceStreamingParams(e) {
    const t = [], n = [];
    for (let s = 0; s < e.length; s++) {
      const r = e[s];
      if (this._isObservable(r)) {
        const c = this._invocationId;
        this._invocationId++, t[c] = r, n.push(c.toString()), e.splice(s, 1);
      }
    }
    return [t, n];
  }
  _isObservable(e) {
    return e && e.subscribe && typeof e.subscribe == "function";
  }
  _createStreamInvocation(e, t, n) {
    const s = this._invocationId;
    return this._invocationId++, n.length !== 0 ? {
      arguments: t,
      invocationId: s.toString(),
      streamIds: n,
      target: e,
      type: u.StreamInvocation
    } : {
      arguments: t,
      invocationId: s.toString(),
      target: e,
      type: u.StreamInvocation
    };
  }
  _createCancelInvocation(e) {
    return {
      invocationId: e,
      type: u.CancelInvocation
    };
  }
  _createStreamItemMessage(e, t) {
    return {
      invocationId: e,
      item: t,
      type: u.StreamItem
    };
  }
  _createCompletionMessage(e, t, n) {
    return t ? {
      error: t,
      invocationId: e,
      type: u.Completion
    } : {
      invocationId: e,
      result: n,
      type: u.Completion
    };
  }
  _createCloseMessage() {
    return { type: u.Close };
  }
}
const Ye = [0, 2e3, 1e4, 3e4, null];
class ce {
  constructor(e) {
    this._retryDelays = e !== void 0 ? [...e, null] : Ye;
  }
  nextRetryDelayInMilliseconds(e) {
    return this._retryDelays[e.previousRetryCount];
  }
}
class R {
}
R.Authorization = "Authorization";
R.Cookie = "Cookie";
class Qe extends W {
  constructor(e, t) {
    super(), this._innerClient = e, this._accessTokenFactory = t;
  }
  async send(e) {
    let t = !0;
    this._accessTokenFactory && (!this._accessToken || e.url && e.url.indexOf("/negotiate?") > 0) && (t = !1, this._accessToken = await this._accessTokenFactory()), this._setAuthorizationHeader(e);
    const n = await this._innerClient.send(e);
    return t && n.statusCode === 401 && this._accessTokenFactory ? (this._accessToken = await this._accessTokenFactory(), this._setAuthorizationHeader(e), await this._innerClient.send(e)) : n;
  }
  _setAuthorizationHeader(e) {
    e.headers || (e.headers = {}), this._accessToken ? e.headers[R.Authorization] = `Bearer ${this._accessToken}` : this._accessTokenFactory && e.headers[R.Authorization] && delete e.headers[R.Authorization];
  }
  getCookieString(e) {
    return this._innerClient.getCookieString(e);
  }
}
var w;
(function(o) {
  o[o.None = 0] = "None", o[o.WebSockets = 1] = "WebSockets", o[o.ServerSentEvents = 2] = "ServerSentEvents", o[o.LongPolling = 4] = "LongPolling";
})(w || (w = {}));
var b;
(function(o) {
  o[o.Text = 1] = "Text", o[o.Binary = 2] = "Binary";
})(b || (b = {}));
let Ze = class {
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
class ae {
  // This is an internal type, not exported from 'index' so this is really just internal.
  get pollAborted() {
    return this._pollAbort.aborted;
  }
  constructor(e, t, n) {
    this._httpClient = e, this._logger = t, this._pollAbort = new Ze(), this._options = n, this._running = !1, this.onreceive = null, this.onclose = null;
  }
  async connect(e, t) {
    if (m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, b, "transferFormat"), this._url = e, this._logger.log(i.Trace, "(LongPolling transport) Connecting."), t === b.Binary && typeof XMLHttpRequest < "u" && typeof new XMLHttpRequest().responseType != "string")
      throw new Error("Binary protocols over XmlHttpRequest not implementing advanced features are not supported.");
    const [n, s] = A(), r = { [n]: s, ...this._options.headers }, c = {
      abortSignal: this._pollAbort.signal,
      headers: r,
      timeout: 1e5,
      withCredentials: this._options.withCredentials
    };
    t === b.Binary && (c.responseType = "arraybuffer");
    const a = `${e}&_=${Date.now()}`;
    this._logger.log(i.Trace, `(LongPolling transport) polling: ${a}.`);
    const l = await this._httpClient.get(a, c);
    l.statusCode !== 200 ? (this._logger.log(i.Error, `(LongPolling transport) Unexpected response code: ${l.statusCode}.`), this._closeError = new P(l.statusText || "", l.statusCode), this._running = !1) : this._running = !0, this._receiving = this._poll(this._url, c);
  }
  async _poll(e, t) {
    try {
      for (; this._running; )
        try {
          const n = `${e}&_=${Date.now()}`;
          this._logger.log(i.Trace, `(LongPolling transport) polling: ${n}.`);
          const s = await this._httpClient.get(n, t);
          s.statusCode === 204 ? (this._logger.log(i.Information, "(LongPolling transport) Poll terminated by server."), this._running = !1) : s.statusCode !== 200 ? (this._logger.log(i.Error, `(LongPolling transport) Unexpected response code: ${s.statusCode}.`), this._closeError = new P(s.statusText || "", s.statusCode), this._running = !1) : s.content ? (this._logger.log(i.Trace, `(LongPolling transport) data received. ${M(s.content, this._options.logMessageContent)}.`), this.onreceive && this.onreceive(s.content)) : this._logger.log(i.Trace, "(LongPolling transport) Poll timed out, reissuing.");
        } catch (n) {
          this._running ? n instanceof G ? this._logger.log(i.Trace, "(LongPolling transport) Poll timed out, reissuing.") : (this._closeError = n, this._running = !1) : this._logger.log(i.Trace, `(LongPolling transport) Poll errored after shutdown: ${n.message}`);
        }
    } finally {
      this._logger.log(i.Trace, "(LongPolling transport) Polling complete."), this.pollAborted || this._raiseOnClose();
    }
  }
  async send(e) {
    return this._running ? pe(this._logger, "LongPolling", this._httpClient, this._url, e, this._options) : Promise.reject(new Error("Cannot send until the transport is connected"));
  }
  async stop() {
    this._logger.log(i.Trace, "(LongPolling transport) Stopping polling."), this._running = !1, this._pollAbort.abort();
    try {
      await this._receiving, this._logger.log(i.Trace, `(LongPolling transport) sending DELETE request to ${this._url}.`);
      const e = {}, [t, n] = A();
      e[t] = n;
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
      r ? r instanceof P && (r.statusCode === 404 ? this._logger.log(i.Trace, "(LongPolling transport) A 404 response was returned from sending a DELETE request.") : this._logger.log(i.Trace, `(LongPolling transport) Error sending a DELETE request: ${r}`)) : this._logger.log(i.Trace, "(LongPolling transport) DELETE request accepted.");
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
class et {
  constructor(e, t, n, s) {
    this._httpClient = e, this._accessToken = t, this._logger = n, this._options = s, this.onreceive = null, this.onclose = null;
  }
  async connect(e, t) {
    return m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, b, "transferFormat"), this._logger.log(i.Trace, "(SSE transport) Connecting."), this._url = e, this._accessToken && (e += (e.indexOf("?") < 0 ? "?" : "&") + `access_token=${encodeURIComponent(this._accessToken)}`), new Promise((n, s) => {
      let r = !1;
      if (t !== b.Text) {
        s(new Error("The Server-Sent Events transport only supports the 'Text' transfer format"));
        return;
      }
      let c;
      if (_.isBrowser || _.isWebWorker)
        c = new this._options.EventSource(e, { withCredentials: this._options.withCredentials });
      else {
        const a = this._httpClient.getCookieString(e), l = {};
        l.Cookie = a;
        const [h, d] = A();
        l[h] = d, c = new this._options.EventSource(e, { withCredentials: this._options.withCredentials, headers: { ...l, ...this._options.headers } });
      }
      try {
        c.onmessage = (a) => {
          if (this.onreceive)
            try {
              this._logger.log(i.Trace, `(SSE transport) data received. ${M(a.data, this._options.logMessageContent)}.`), this.onreceive(a.data);
            } catch (l) {
              this._close(l);
              return;
            }
        }, c.onerror = (a) => {
          r ? this._close() : s(new Error("EventSource failed to connect. The connection could not be found on the server, either the connection ID is not present on the server, or a proxy is refusing/buffering the connection. If you have multiple servers check that sticky sessions are enabled."));
        }, c.onopen = () => {
          this._logger.log(i.Information, `SSE connected to ${this._url}`), this._eventSource = c, r = !0, n();
        };
      } catch (a) {
        s(a);
        return;
      }
    });
  }
  async send(e) {
    return this._eventSource ? pe(this._logger, "SSE", this._httpClient, this._url, e, this._options) : Promise.reject(new Error("Cannot send until the transport is connected"));
  }
  stop() {
    return this._close(), Promise.resolve();
  }
  _close(e) {
    this._eventSource && (this._eventSource.close(), this._eventSource = void 0, this.onclose && this.onclose(e));
  }
}
class tt {
  constructor(e, t, n, s, r, c) {
    this._logger = n, this._accessTokenFactory = t, this._logMessageContent = s, this._webSocketConstructor = r, this._httpClient = e, this.onreceive = null, this.onclose = null, this._headers = c;
  }
  async connect(e, t) {
    m.isRequired(e, "url"), m.isRequired(t, "transferFormat"), m.isIn(t, b, "transferFormat"), this._logger.log(i.Trace, "(WebSockets transport) Connecting.");
    let n;
    return this._accessTokenFactory && (n = await this._accessTokenFactory()), new Promise((s, r) => {
      e = e.replace(/^http/, "ws");
      let c;
      const a = this._httpClient.getCookieString(e);
      let l = !1;
      if (_.isNode || _.isReactNative) {
        const h = {}, [d, y] = A();
        h[d] = y, n && (h[R.Authorization] = `Bearer ${n}`), a && (h[R.Cookie] = a), c = new this._webSocketConstructor(e, void 0, {
          headers: { ...h, ...this._headers }
        });
      } else
        n && (e += (e.indexOf("?") < 0 ? "?" : "&") + `access_token=${encodeURIComponent(n)}`);
      c || (c = new this._webSocketConstructor(e)), t === b.Binary && (c.binaryType = "arraybuffer"), c.onopen = (h) => {
        this._logger.log(i.Information, `WebSocket connected to ${e}.`), this._webSocket = c, l = !0, s();
      }, c.onerror = (h) => {
        let d = null;
        typeof ErrorEvent < "u" && h instanceof ErrorEvent ? d = h.error : d = "There was an error with the transport", this._logger.log(i.Information, `(WebSockets transport) ${d}.`);
      }, c.onmessage = (h) => {
        if (this._logger.log(i.Trace, `(WebSockets transport) data received. ${M(h.data, this._logMessageContent)}.`), this.onreceive)
          try {
            this.onreceive(h.data);
          } catch (d) {
            this._close(d);
            return;
          }
      }, c.onclose = (h) => {
        if (l)
          this._close(h);
        else {
          let d = null;
          typeof ErrorEvent < "u" && h instanceof ErrorEvent ? d = h.error : d = "WebSocket failed to connect. The connection could not be found on the server, either the endpoint may not be a SignalR endpoint, the connection ID is not present on the server, or there is a proxy blocking WebSockets. If you have multiple servers check that sticky sessions are enabled.", r(new Error(d));
        }
      };
    });
  }
  send(e) {
    return this._webSocket && this._webSocket.readyState === this._webSocketConstructor.OPEN ? (this._logger.log(i.Trace, `(WebSockets transport) sending data. ${M(e, this._logMessageContent)}.`), this._webSocket.send(e), Promise.resolve()) : Promise.reject("WebSocket is not in the OPEN state");
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
const le = 100;
class nt {
  constructor(e, t = {}) {
    if (this._stopPromiseResolver = () => {
    }, this.features = {}, this._negotiateVersion = 1, m.isRequired(e, "url"), this._logger = Ne(t.logger), this.baseUrl = this._resolveUrl(e), t = t || {}, t.logMessageContent = t.logMessageContent === void 0 ? !1 : t.logMessageContent, typeof t.withCredentials == "boolean" || t.withCredentials === void 0)
      t.withCredentials = t.withCredentials === void 0 ? !0 : t.withCredentials;
    else
      throw new Error("withCredentials option was not a 'boolean' or 'undefined' value");
    t.timeout = t.timeout === void 0 ? 100 * 1e3 : t.timeout;
    let n = null, s = null;
    if (_.isNode && typeof require < "u") {
      const r = typeof __webpack_require__ == "function" ? __non_webpack_require__ : require;
      n = r("ws"), s = r("eventsource");
    }
    !_.isNode && typeof WebSocket < "u" && !t.WebSocket ? t.WebSocket = WebSocket : _.isNode && !t.WebSocket && n && (t.WebSocket = n), !_.isNode && typeof EventSource < "u" && !t.EventSource ? t.EventSource = EventSource : _.isNode && !t.EventSource && typeof s < "u" && (t.EventSource = s), this._httpClient = new Qe(t.httpClient || new qe(this._logger), t.accessTokenFactory), this._connectionState = "Disconnected", this._connectionStarted = !1, this._options = t, this.onreceive = null, this.onclose = null;
  }
  async start(e) {
    if (e = e || b.Binary, m.isIn(e, b, "transferFormat"), this._logger.log(i.Debug, `Starting connection with transfer format '${b[e]}'.`), this._connectionState !== "Disconnected")
      return Promise.reject(new Error("Cannot start an HttpConnection that is not in the 'Disconnected' state."));
    if (this._connectionState = "Connecting", this._startInternalPromise = this._startInternal(e), await this._startInternalPromise, this._connectionState === "Disconnecting") {
      const t = "Failed to start the HttpConnection before stop() was called.";
      return this._logger.log(i.Error, t), await this._stopPromise, Promise.reject(new k(t));
    } else if (this._connectionState !== "Connected") {
      const t = "HttpConnection.startInternal completed gracefully but didn't enter the connection into the connected state!";
      return this._logger.log(i.Error, t), Promise.reject(new k(t));
    }
    this._connectionStarted = !0;
  }
  send(e) {
    return this._connectionState !== "Connected" ? Promise.reject(new Error("Cannot send data if the connection is not in the 'Connected' State.")) : (this._sendQueue || (this._sendQueue = new Q(this.transport)), this._sendQueue.send(e));
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
        let n = null, s = 0;
        do {
          if (n = await this._getNegotiationResponse(t), this._connectionState === "Disconnecting" || this._connectionState === "Disconnected")
            throw new k("The connection was stopped during negotiation.");
          if (n.error)
            throw new Error(n.error);
          if (n.ProtocolVersion)
            throw new Error("Detected a connection attempt to an ASP.NET SignalR Server. This client only supports connecting to an ASP.NET Core SignalR Server. See https://aka.ms/signalr-core-differences for details.");
          if (n.url && (t = n.url), n.accessToken) {
            const r = n.accessToken;
            this._accessTokenFactory = () => r, this._httpClient._accessToken = r, this._httpClient._accessTokenFactory = void 0;
          }
          s++;
        } while (n.url && s < le);
        if (s === le && n.url)
          throw new Error("Negotiate redirection limit exceeded.");
        await this._createTransport(t, this._options.transport, n, e);
      }
      this.transport instanceof ae && (this.features.inherentKeepAlive = !0), this._connectionState === "Connecting" && (this._logger.log(i.Debug, "The HttpConnection connected successfully."), this._connectionState = "Connected");
    } catch (n) {
      return this._logger.log(i.Error, "Failed to start the connection: " + n), this._connectionState = "Disconnected", this.transport = void 0, this._stopPromiseResolver(), Promise.reject(n);
    }
  }
  async _getNegotiationResponse(e) {
    const t = {}, [n, s] = A();
    t[n] = s;
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
      return (!a.negotiateVersion || a.negotiateVersion < 1) && (a.connectionToken = a.connectionId), a.useStatefulReconnect && this._options._useStatefulReconnect !== !0 ? Promise.reject(new re("Client didn't negotiate Stateful Reconnect but the server did.")) : a;
    } catch (c) {
      let a = "Failed to complete negotiation with the server: " + c;
      return c instanceof P && c.statusCode === 404 && (a = a + " Either this is not a SignalR endpoint or there is a proxy blocking the connection."), this._logger.log(i.Error, a), Promise.reject(new re(a));
    }
  }
  _createConnectUrl(e, t) {
    return t ? e + (e.indexOf("?") === -1 ? "?" : "&") + `id=${t}` : e;
  }
  async _createTransport(e, t, n, s) {
    let r = this._createConnectUrl(e, n.connectionToken);
    if (this._isITransport(t)) {
      this._logger.log(i.Debug, "Connection was provided an instance of ITransport, using that directly."), this.transport = t, await this._startTransport(r, s), this.connectionId = n.connectionId;
      return;
    }
    const c = [], a = n.availableTransports || [];
    let l = n;
    for (const h of a) {
      const d = this._resolveTransportOrError(h, t, s, l?.useStatefulReconnect === !0);
      if (d instanceof Error)
        c.push(`${h.transport} failed:`), c.push(d);
      else if (this._isITransport(d)) {
        if (this.transport = d, !l) {
          try {
            l = await this._getNegotiationResponse(e);
          } catch (y) {
            return Promise.reject(y);
          }
          r = this._createConnectUrl(e, l.connectionToken);
        }
        try {
          await this._startTransport(r, s), this.connectionId = l.connectionId;
          return;
        } catch (y) {
          if (this._logger.log(i.Error, `Failed to start the transport '${h.transport}': ${y}`), l = void 0, c.push(new $e(`${h.transport} failed: ${y}`, w[h.transport])), this._connectionState !== "Connecting") {
            const I = "Failed to select transport before stop() was called.";
            return this._logger.log(i.Debug, I), Promise.reject(new k(I));
          }
        }
      }
    }
    return c.length > 0 ? Promise.reject(new xe(`Unable to connect to the server with any of the available transports. ${c.join(" ")}`, c)) : Promise.reject(new Error("None of the transports supported by the client are supported by the server."));
  }
  _constructTransport(e) {
    switch (e) {
      case w.WebSockets:
        if (!this._options.WebSocket)
          throw new Error("'WebSocket' is not supported in your environment.");
        return new tt(this._httpClient, this._accessTokenFactory, this._logger, this._options.logMessageContent, this._options.WebSocket, this._options.headers || {});
      case w.ServerSentEvents:
        if (!this._options.EventSource)
          throw new Error("'EventSource' is not supported in your environment.");
        return new et(this._httpClient, this._httpClient._accessToken, this._logger, this._options);
      case w.LongPolling:
        return new ae(this._httpClient, this._logger, this._options);
      default:
        throw new Error(`Unknown transport: ${e}.`);
    }
  }
  _startTransport(e, t) {
    return this.transport.onreceive = this.onreceive, this.features.reconnect ? this.transport.onclose = async (n) => {
      let s = !1;
      if (this.features.reconnect)
        try {
          this.features.disconnected(), await this.transport.connect(e, t), await this.features.resend();
        } catch {
          s = !0;
        }
      else {
        this._stopConnection(n);
        return;
      }
      s && this._stopConnection(n);
    } : this.transport.onclose = (n) => this._stopConnection(n), this.transport.connect(e, t);
  }
  _resolveTransportOrError(e, t, n, s) {
    const r = w[e.transport];
    if (r == null)
      return this._logger.log(i.Debug, `Skipping transport '${e.transport}' because it is not supported by this client.`), new Error(`Skipping transport '${e.transport}' because it is not supported by this client.`);
    if (ot(t, r))
      if (e.transferFormats.map((a) => b[a]).indexOf(n) >= 0) {
        if (r === w.WebSockets && !this._options.WebSocket || r === w.ServerSentEvents && !this._options.EventSource)
          return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it is not supported in your environment.'`), new Pe(`'${w[r]}' is not supported in your environment.`, r);
        this._logger.log(i.Debug, `Selecting transport '${w[r]}'.`);
        try {
          return this.features.reconnect = r === w.WebSockets ? s : void 0, this._constructTransport(r);
        } catch (a) {
          return a;
        }
      } else
        return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it does not support the requested transfer format '${b[n]}'.`), new Error(`'${w[r]}' does not support ${b[n]}.`);
    else
      return this._logger.log(i.Debug, `Skipping transport '${w[r]}' because it was disabled by the client.`), new Re(`'${w[r]}' is disabled by the client.`, r);
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
    const n = new URLSearchParams(t.searchParams);
    return n.has("negotiateVersion") || n.append("negotiateVersion", this._negotiateVersion.toString()), n.has("useStatefulReconnect") ? n.get("useStatefulReconnect") === "true" && (this._options._useStatefulReconnect = !0) : this._options._useStatefulReconnect === !0 && n.append("useStatefulReconnect", "true"), t.search = n.toString(), t.toString();
  }
}
function ot(o, e) {
  return !o || (e & o) !== 0;
}
class Q {
  constructor(e) {
    this._transport = e, this._buffer = [], this._executing = !0, this._sendBufferedData = new j(), this._transportResult = new j(), this._sendLoopPromise = this._sendLoop();
  }
  send(e) {
    return this._bufferData(e), this._transportResult || (this._transportResult = new j()), this._transportResult.promise;
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
      this._sendBufferedData = new j();
      const e = this._transportResult;
      this._transportResult = void 0;
      const t = typeof this._buffer[0] == "string" ? this._buffer.join("") : Q._concatBuffers(this._buffer);
      this._buffer.length = 0;
      try {
        await this._transport.send(t), e.resolve();
      } catch (n) {
        e.reject(n);
      }
    }
  }
  static _concatBuffers(e) {
    const t = e.map((r) => r.byteLength).reduce((r, c) => r + c), n = new Uint8Array(t);
    let s = 0;
    for (const r of e)
      n.set(new Uint8Array(r), s), s += r.byteLength;
    return n.buffer;
  }
}
class j {
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
const st = "json";
class rt {
  constructor() {
    this.name = st, this.version = 2, this.transferFormat = b.Text;
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
    t === null && (t = H.instance);
    const n = C.parse(e), s = [];
    for (const r of n) {
      const c = JSON.parse(r);
      if (typeof c.type != "number")
        throw new Error("Invalid payload.");
      switch (c.type) {
        case u.Invocation:
          this._isInvocationMessage(c);
          break;
        case u.StreamItem:
          this._isStreamItemMessage(c);
          break;
        case u.Completion:
          this._isCompletionMessage(c);
          break;
        case u.Ping:
          break;
        case u.Close:
          break;
        case u.Ack:
          this._isAckMessage(c);
          break;
        case u.Sequence:
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
    return C.write(JSON.stringify(e));
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
const it = {
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
function ct(o) {
  const e = it[o.toLowerCase()];
  if (typeof e < "u")
    return e;
  throw new Error(`Unknown log level: ${o}`);
}
class at {
  configureLogging(e) {
    if (m.isRequired(e, "logging"), lt(e))
      this.logger = e;
    else if (typeof e == "string") {
      const t = ct(e);
      this.logger = new L(t);
    } else
      this.logger = new L(e);
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
    return e ? Array.isArray(e) ? this.reconnectPolicy = new ce(e) : this.reconnectPolicy = e : this.reconnectPolicy = new ce(), this;
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
    const t = new nt(this.url, e);
    return Y.create(t, this.logger || H.instance, this.protocol || new rt(), this.reconnectPolicy, this._serverTimeoutInMilliseconds, this._keepAliveIntervalInMilliseconds, this._statefulReconnectBufferSize);
  }
}
function lt(o) {
  return o.log !== void 0;
}
const me = "/_elsa/studio/diagnostics/console-logs", ht = `${me}/recent`, ut = `${me}/hub`, he = 2e3, we = "elsa-studio-console-stream-autoscroll", V = /\x1b\[([0-9;]*)m/g, ue = {
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
}, de = {
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
let be;
function vt(o) {
  be = o, o.panels.add({
    id: "console-stream",
    title: "Console",
    order: 1e3,
    component: dt
  });
}
function dt() {
  const [o, e] = T([]), [t, n] = T(0), [s, r] = T(!1), [c, a] = T(!1), [l, h] = T(null), [d, y] = T(0), [I, Ce] = T(St), B = ne(/* @__PURE__ */ new Set()), Z = ne(null), U = c ? l ?? o : o, ee = c ? Math.max(0, t - d) : 0, x = F((f) => {
    const v = [];
    for (const S of f)
      B.current.has(S.id) || (B.current.add(S.id), v.push(S));
    v.length !== 0 && (n((S) => S + v.length), e((S) => _t(S, v)));
  }, []), D = F((f, v) => {
    x([pt(f, v)]);
  }, [x]), O = F(async () => {
    const f = await be.host.http.getJson(`${ht}?limit=${he}`), v = f.items ?? f.lines ?? [];
    x(v.map(ge));
  }, [x]);
  z(() => {
    window.localStorage.setItem(we, String(I));
  }, [I]), z(() => {
    if (!I || c)
      return;
    const f = Z.current;
    f && (f.scrollTop = f.scrollHeight);
  }, [I, c, U]), z(() => {
    let f = !1, v = null;
    const S = new at().withUrl(ut).withAutomaticReconnect().build();
    S.onreconnecting(() => r(!1)), S.onreconnected(() => r(!0)), S.onclose(() => r(!1));
    async function ke() {
      try {
        if (await S.start(), f)
          return;
        r(!0), v = S.stream("StreamAsync", { limit: he }).subscribe({
          next: (E) => {
            if (E?.line)
              x([ge(E.line)]);
            else if (E?.droppedLines || E?.dropped) {
              const q = E.droppedLines ?? E.dropped;
              D("stderr", `${q?.count ?? 0} console lines were dropped.`);
            }
          },
          error: (E) => D("stderr", `Console stream failed: ${K(E)}`),
          complete: () => D("stdout", "Console stream completed.")
        }), await O();
      } catch (E) {
        r(!1), D("stderr", `Console stream connection failed: ${K(E)}`), await O().catch((q) => D("stderr", `Recent console lines failed: ${K(q)}`));
      }
    }
    return ke(), () => {
      f = !0, v?.dispose(), S.stop();
    };
  }, [x, D, O]);
  const te = Ee(
    () => U.map((f) => ({ ...f, renderedText: wt(f.text) })),
    [U]
  );
  function ye() {
    if (c) {
      a(!1), h(null);
      return;
    }
    h(o), y(t), a(!0);
  }
  function ve() {
    e([]), n(0), h(c ? [] : null), y(0), B.current.clear();
  }
  return /* @__PURE__ */ p.jsxs("section", { className: "console-stream-panel", children: [
    /* @__PURE__ */ p.jsxs("header", { className: "console-stream-header", children: [
      /* @__PURE__ */ p.jsxs("div", { children: [
        /* @__PURE__ */ p.jsx("h2", { children: "Backend console" }),
        ee > 0 ? /* @__PURE__ */ p.jsxs("p", { children: [
          ee,
          " buffered while paused"
        ] }) : null
      ] }),
      /* @__PURE__ */ p.jsxs("div", { className: "console-stream-tools", children: [
        /* @__PURE__ */ p.jsx("span", { className: s ? "console-stream-status online" : "console-stream-status" }),
        /* @__PURE__ */ p.jsx("span", { children: s ? "live" : "waiting" }),
        /* @__PURE__ */ p.jsx("span", { children: "stdout" }),
        /* @__PURE__ */ p.jsx("span", { children: "stderr" }),
        /* @__PURE__ */ p.jsx("button", { type: "button", className: c ? "active" : "", onClick: ye, "aria-pressed": c, children: c ? "Resume" : "Pause" }),
        /* @__PURE__ */ p.jsx("button", { type: "button", className: I ? "active" : "", onClick: () => Ce((f) => !f), "aria-pressed": I, children: "Autoscroll" }),
        /* @__PURE__ */ p.jsx("button", { type: "button", onClick: ve, children: "Clear" })
      ] })
    ] }),
    /* @__PURE__ */ p.jsxs("div", { className: "console-stream-lines", ref: Z, children: [
      te.length === 0 ? /* @__PURE__ */ p.jsxs("div", { className: "console-stream-line stdout", children: [
        /* @__PURE__ */ p.jsx("span", { children: (/* @__PURE__ */ new Date()).toLocaleTimeString() }),
        /* @__PURE__ */ p.jsx("code", { children: "Console stream is ready." })
      ] }) : null,
      te.map((f) => /* @__PURE__ */ p.jsxs("div", { className: `console-stream-line ${f.stream}`, children: [
        /* @__PURE__ */ p.jsx("span", { children: new Date(f.timestamp).toLocaleTimeString() }),
        /* @__PURE__ */ p.jsx("code", { children: f.renderedText })
      ] }, f.id))
    ] })
  ] });
}
function gt(o) {
  return o === 1 || o === "stderr" || o === "Stderr" ? "stderr" : "stdout";
}
function ft(o, e) {
  const t = Date.parse(o.timestamp) - Date.parse(e.timestamp);
  return t !== 0 ? t : (o.sequence ?? 0) - (e.sequence ?? 0);
}
function _t(o, e) {
  return [...o, ...e].sort(ft).slice(-2e3);
}
function pt(o, e) {
  return {
    id: `${Date.now()}-${Math.random()}`,
    timestamp: (/* @__PURE__ */ new Date()).toISOString(),
    sequence: null,
    stream: o,
    text: e
  };
}
function ge(o) {
  return {
    id: o.id ?? `${o.sequence ?? Date.now()}-${Math.random()}`,
    timestamp: o.timestamp ?? o.receivedAt ?? (/* @__PURE__ */ new Date()).toISOString(),
    sequence: o.sequence ?? null,
    stream: gt(o.stream),
    text: o.text ?? ""
  };
}
function mt(o) {
  if (!o.includes("\x1B["))
    return [{ text: o, className: "" }];
  const e = [], t = Se();
  let n = 0, s;
  for (V.lastIndex = 0; (s = V.exec(o)) !== null; ) {
    s.index > n && e.push({ text: o.slice(n, s.index), className: fe(t) });
    const r = s[1] === "" ? [0] : s[1].split(";").map((c) => Number(c || 0));
    bt(t, r), n = V.lastIndex;
  }
  return n < o.length && e.push({ text: o.slice(n), className: fe(t) }), e;
}
function wt(o) {
  return mt(o).map((e, t) => e.className ? /* @__PURE__ */ p.jsx("span", { className: e.className, children: e.text }, t) : e.text);
}
function Se() {
  return {
    bold: !1,
    dim: !1,
    foreground: "",
    background: ""
  };
}
function bt(o, e) {
  for (const t of e)
    t === 0 ? Object.assign(o, Se()) : t === 1 ? (o.bold = !0, o.dim = !1) : t === 2 ? (o.dim = !0, o.bold = !1) : t === 22 ? (o.bold = !1, o.dim = !1) : t === 39 ? o.foreground = "" : t === 49 ? o.background = "" : ue[t] ? o.foreground = ue[t] : de[t] && (o.background = de[t]);
}
function fe(o) {
  return [
    o.bold ? "console-stream-ansi-bold" : "",
    o.dim ? "console-stream-ansi-dim" : "",
    o.foreground,
    o.background
  ].filter(Boolean).join(" ");
}
function St() {
  return typeof window > "u" ? !0 : window.localStorage.getItem(we) !== "false";
}
function K(o) {
  return o instanceof Error ? o.message : String(o);
}
export {
  dt as ConsoleStreamPanel,
  _t as appendConsoleEntries,
  ft as compareConsoleEntries,
  pt as createConsoleEntry,
  ge as createConsoleEntryFromLine,
  gt as getConsoleStreamName,
  mt as parseAnsiSegments,
  vt as register
};
