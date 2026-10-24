(() => {
  "use strict";

  const $ = (id) => document.getElementById(id);
  const brandIcon = $("brand-icon");
  const brandFallback = $("brand-fallback");
  brandFallback.hidden = true;
  brandIcon.addEventListener("error", () => {
    brandIcon.hidden = true;
    brandFallback.hidden = false;
  });
  brandIcon.addEventListener("load", () => {
    brandIcon.hidden = false;
    brandFallback.hidden = true;
  });

  const ui = {
    connection: $("connection-status"), connectionLabel: $("connection-label"),
    serviceName: $("service-name"), serviceEndpoint: $("service-endpoint"),
    serviceAuth: $("service-auth"), serviceStarted: $("service-started"),
    snapshotStatus: $("snapshot-status"), refresh: $("refresh"),
    requestList: $("request-list"), listEmpty: $("list-empty"),
    selectionMode: $("selection-mode"), requestPlaceholder: $("request-placeholder"),
    requestDetail: $("request-detail"), method: $("request-method"), status: $("request-status"),
    path: $("request-path"), duration: $("request-duration"), authDecision: $("auth-decision"),
    authHelp: $("auth-help"), authValue: $("auth-value"), authVersion: $("auth-version"),
    authFingerprint: $("auth-fingerprint"), reveal: $("reveal"),
    requestHeaders: $("request-headers"), requestBody: $("request-body"),
    responseHeaders: $("response-headers"), responseBody: $("response-body"),
    quoteForm: $("quote-form"), subtotal: $("subtotal"), shipping: $("shipping"),
    quoteSubmit: $("quote-submit"), quoteOutcome: $("quote-outcome"), quoteTotal: $("quote-total"),
    quoteFeedback: $("quote-feedback"), quoteDisclosure: $("quote-disclosure"),
    quoteRequest: $("quote-request"), quoteResponse: $("quote-response"),
  };

  const buttons = new Map();
  let requests = [];
  let requestsById = new Map();
  let selectedId = null;
  let followingLatest = true;
  let lastLatestId = null;
  let revealedValue = null;
  let revealTimer = 0;
  let polling = false;
  let quotePending = false;
  let lastSnapshotAt = null;

  const text = (value, fallback = "Not observed") => {
    if (value === null || value === undefined || value === "") return fallback;
    if (typeof value === "string") return value;
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    return fallback;
  };

  const safeText = (value, fallback = "Not observed") => text(value, fallback);
  const formatTime = (value) => {
    if (!value) return "Not observed";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? safeText(value) : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  };
  const formatDuration = (value) => typeof value === "number" && Number.isFinite(value) ? value >= 1000 ? `${(value / 1000).toFixed(1)} s` : `${value.toFixed(value < 10 ? 1 : 0)} ms` : "Not observed";
  const formatStatus = (value) => value === null || value === undefined ? "Not observed" : String(value);
  const isSuccessful = (value) => Number(value) >= 200 && Number(value) < 300;
  const isAuthHeader = (name) => String(name).toLowerCase() === "authorization";
  const sensitiveKey = (key) => /authorization|access.?token|refresh.?token|secret|password|cookie|api.?key/i.test(String(key));

  function scrub(value, key = "") {
    if (sensitiveKey(key)) return "[redacted]";
    if (Array.isArray(value)) return value.map((item) => scrub(item));
    if (value && typeof value === "object") {
      const clean = {};
      for (const [childKey, childValue] of Object.entries(value)) clean[childKey] = scrub(childValue, childKey);
      return clean;
    }
    if (typeof value === "string") {
      return value
        .replace(/(Bearer\s+)[A-Za-z0-9._~+\/-]+=*/gi, "$1[redacted]")
        .replace(/((?:token|secret|password|api[_-]?key)=)[^&\s]+/gi, "$1[redacted]");
    }
    return value;
  }

  function jsonText(value) {
    if (value === null || value === undefined) return "Not observed";
    try { return JSON.stringify(scrub(value), null, 2); }
    catch { return "Not observed"; }
  }

  function renderHeaders(target, headers) {
    target.replaceChildren();
    if (!headers || typeof headers !== "object" || Array.isArray(headers) || Object.keys(headers).length === 0) {
      const empty = document.createElement("div");
      const term = document.createElement("dt");
      const desc = document.createElement("dd");
      term.textContent = "Headers";
      desc.textContent = "Not observed";
      empty.append(term, desc);
      target.append(empty);
      return;
    }
    for (const [name, value] of Object.entries(headers)) {
      const row = document.createElement("div");
      const term = document.createElement("dt");
      const desc = document.createElement("dd");
      term.textContent = safeText(name);
      desc.textContent = isAuthHeader(name) ? maskAuthorization(value) : sensitiveKey(name) ? "[redacted]" : safeText(scrub(value));
      row.append(term, desc);
      target.append(row);
    }
  }

  function maskAuthorization(value) {
    const candidate = typeof value === "string" ? value.trim().split(/\s+/, 1)[0] : "Bearer";
    const scheme = /^(bearer|basic|token)$/i.test(candidate) ? candidate : "Credential";
    return `${scheme} ••••••••`;
  }

  function hideRevealedAuthorization(update = true) {
    if (revealTimer) window.clearTimeout(revealTimer);
    revealTimer = 0;
    revealedValue = null;
    if (ui.authValue) ui.authValue.textContent = selectedId ? "Bearer ••••••••" : "Not observed";
    if (ui.reveal) ui.reveal.textContent = "Reveal demo key";
    if (update && selectedId) renderSelectedRequest();
  }

  function setConnection(kind, label) {
    ui.connection.classList.remove("loading", "connected", "disconnected");
    ui.connection.classList.add(kind);
    ui.connectionLabel.textContent = label;
  }

  function updateService(service) {
    const item = service && typeof service === "object" ? service : {};
    ui.serviceName.textContent = safeText(item.name);
    ui.serviceEndpoint.textContent = safeText(item.endpoint);
    ui.serviceAuth.textContent = safeText(item.authScheme);
    ui.serviceStarted.textContent = item.startedAt ? formatTime(item.startedAt) : "Not observed";
  }

  function requestButton(request) {
    const id = String(request.id);
    let button = buttons.get(id);
    if (!button) {
      button = document.createElement("button");
      button.type = "button";
      button.className = "request-row";
      button.id = `request-${id.replace(/[^a-zA-Z0-9_-]/g, "_")}`;
      button.setAttribute("aria-pressed", "false");
      const top = document.createElement("span"); top.className = "row-top";
      const method = document.createElement("span"); method.className = "row-method";
      const status = document.createElement("span"); status.className = "row-status";
      top.append(method, status);
      const route = document.createElement("span"); route.className = "row-route mono";
      const bottom = document.createElement("span"); bottom.className = "row-bottom";
      const time = document.createElement("span"); time.className = "row-time";
      const duration = document.createElement("span"); duration.className = "row-time";
      bottom.append(time, duration);
      button.append(top, route, bottom);
      button.addEventListener("click", () => selectRequest(button.dataset.requestId, false));
      button.addEventListener("keydown", onRequestKeydown);
      buttons.set(id, button);
    }
    button.dataset.requestId = id;
    const spans = button.querySelectorAll(".row-method, .row-status, .row-route, .row-time");
    spans[0].textContent = safeText(request.method).toUpperCase();
    spans[1].textContent = formatStatus(request.status);
    spans[2].textContent = safeText(request.path);
    spans[3].textContent = formatTime(request.timestamp);
    spans[4].textContent = formatDuration(request.durationMs);
    button.setAttribute("aria-label", `${safeText(request.method)} ${safeText(request.path)}, status ${formatStatus(request.status)}, ${formatTime(request.timestamp)}`);
    return button;
  }

  function onRequestKeydown(event) {
    const rows = Array.from(ui.requestList.querySelectorAll("button.request-row"));
    const index = rows.indexOf(event.currentTarget);
    let next = -1;
    if (event.key === "ArrowDown" || event.key === "ArrowRight") next = Math.min(rows.length - 1, index + 1);
    if (event.key === "ArrowUp" || event.key === "ArrowLeft") next = Math.max(0, index - 1);
    if (event.key === "Home") next = 0;
    if (event.key === "End") next = rows.length - 1;
    if (next >= 0 && rows[next]) {
      event.preventDefault();
      rows[next].focus();
      rows[next].click();
    }
  }

  function renderRequestList() {
    const wanted = new Set();
    const ordered = requests.map((request) => {
      const button = requestButton(request);
      wanted.add(String(request.id));
      button.setAttribute("aria-pressed", String(String(request.id) === selectedId));
      return button;
    });
    for (const [id, button] of buttons) {
      if (!wanted.has(id)) {
        button.remove();
        buttons.delete(id);
      }
    }
    // Reorder existing nodes only when the observed order changes. Reusing nodes
    // keeps the current focus and selection intact during one-second polling.
    for (let i = 0; i < ordered.length; i++) {
      const current = ui.requestList.children[i];
      if (current !== ordered[i]) ui.requestList.insertBefore(ordered[i], current || null);
    }
    ui.listEmpty.hidden = requests.length > 0;
  }

  function renderSelectedRequest() {
    const request = selectedId ? requestsById.get(selectedId) : null;
    const hasRequest = Boolean(request);
    ui.requestPlaceholder.hidden = hasRequest;
    ui.requestDetail.hidden = !hasRequest;
    if (!request) {
      ui.selectionMode.textContent = "Waiting for observed traffic";
      ui.selectionMode.className = "mode-label";
      return;
    }
    const isLatest = requests.length > 0 && String(requests[0].id) === selectedId;
    ui.selectionMode.textContent = isLatest ? "Latest observed request" : "Inspecting history";
    ui.selectionMode.className = `mode-label ${isLatest ? "live" : "history"}`;
    ui.method.textContent = safeText(request.method).toUpperCase();
    ui.status.textContent = formatStatus(request.status);
    const hasStatus = request.status !== null && request.status !== undefined;
    ui.status.className = `http-badge ${hasStatus ? (isSuccessful(request.status) ? "ok" : "error") : ""}`;
    ui.path.textContent = safeText(request.path);
    ui.duration.textContent = formatDuration(request.durationMs);

    const auth = request.authentication && typeof request.authentication === "object" ? request.authentication : {};
    const hasDecision = typeof auth.accepted === "boolean";
    ui.authDecision.textContent = hasDecision ? (auth.accepted ? "Accepted" : "Rejected") : "Not observed";
    ui.authDecision.className = `auth-decision ${hasDecision ? (auth.accepted ? "accepted" : "rejected") : "unknown"}`;
    ui.authVersion.textContent = safeText(auth.version);
    ui.authFingerprint.textContent = safeText(auth.fingerprint);
    const authorization = request.headers && typeof request.headers === "object"
      ? Object.entries(request.headers).find(([name]) => isAuthHeader(name))?.[1]
      : null;
    ui.authValue.textContent = revealedValue && revealedValue.requestId === selectedId
      ? revealedValue.value
      : authorization ? maskAuthorization(authorization) : "Not observed";
    ui.authHelp.textContent = revealedValue && revealedValue.requestId === selectedId
      ? "Visible for this request. It will hide automatically in 15 seconds or when the selection changes."
      : "Credential values stay hidden until you request a reveal.";
    ui.reveal.disabled = auth.canReveal !== true || !authorization;
    if (revealedValue && revealedValue.requestId === selectedId) ui.reveal.textContent = "Hide credential";

    renderHeaders(ui.requestHeaders, request.headers);
    ui.requestBody.textContent = request.body == null && request.method === "GET" ? "No body (GET request)" : jsonText(request.body);
    const response = request.response && typeof request.response === "object" ? request.response : {};
    renderHeaders(ui.responseHeaders, response.headers);
    ui.responseBody.textContent = jsonText(response.body);
  }

  function selectRequest(id, follow) {
    if (!id || !requestsById.has(String(id))) return;
    const next = String(id);
    if (next !== selectedId) hideRevealedAuthorization(false);
    selectedId = next;
    followingLatest = Boolean(follow || (requests.length && next === String(requests[0].id)));
    renderRequestList();
    renderSelectedRequest();
  }

  function renderQuote(quote) {
    if (!quote || typeof quote !== "object") return;
    const status = quote.status;
    const hasStatus = status !== null && status !== undefined;
    const successful = hasStatus && isSuccessful(status) && quote.responseValid !== false;
    const body = quote.body;
    const resultBody = body && typeof body === "object" ? body : {};
    ui.quoteOutcome.textContent = !hasStatus
      ? "Quote result observed"
      : `HTTP ${formatStatus(status)} · ${successful ? "completed" : "failed"}`;
    ui.quoteOutcome.style.color = successful ? "" : "var(--bad)";
    const total = resultBody.total;
    ui.quoteTotal.textContent = typeof total === "number" || typeof total === "string" ? safeText(total) : "—";
    const duration = formatDuration(quote.durationMs);
    ui.quoteFeedback.textContent = successful
      ? `Customer quote returned${duration !== "Not observed" ? ` in ${duration}` : ""}.`
      : quote.responseValid === false
        ? "The workflow returned an invalid JSON response. Check the workflow response settings."
        : hasStatus
        ? `Workflow response status: ${formatStatus(status)}. Review the observed response below.`
        : "Workflow response status was not observed. Review the captured result below.";
    ui.quoteRequest.textContent = jsonText(quote.request);
    ui.quoteResponse.textContent = jsonText({ status: quote.status, body: quote.body });
    ui.quoteDisclosure.hidden = false;
  }

  function renderState(state) {
    const nextRequests = Array.isArray(state.requests) ? state.requests.filter((request) => request && request.id !== null && request.id !== undefined) : [];
    const newLatestId = nextRequests.length ? String(nextRequests[0].id) : null;
    if (lastLatestId !== null && newLatestId !== null && newLatestId !== lastLatestId) hideRevealedAuthorization(false);
    lastLatestId = newLatestId;
    requests = nextRequests;
    requestsById = new Map(requests.map((request) => [String(request.id), request]));
    updateService(state.service);
    if (followingLatest && requests.length) selectedId = String(requests[0].id);
    else if (selectedId && !requestsById.has(selectedId)) {
      selectedId = requests.length ? String(requests[0].id) : null;
      followingLatest = true;
      hideRevealedAuthorization(false);
    } else if (!selectedId && requests.length) selectedId = String(requests[0].id);
    renderRequestList();
    renderSelectedRequest();
    if (state.quote && !quotePending) renderQuote(state.quote);
    lastSnapshotAt = new Date();
    ui.snapshotStatus.textContent = `Updated ${lastSnapshotAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
    setConnection("connected", "Connected · live state");
  }

  async function pollState() {
    if (polling) return;
    polling = true;
    try {
      const response = await fetch("/cockpit/state", { cache: "no-store", headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(`State request returned HTTP ${response.status}`);
      const state = await response.json();
      if (!state || typeof state !== "object") throw new Error("Invalid state response");
      renderState(state);
    } catch {
      setConnection("disconnected", "Disconnected · retrying");
      ui.snapshotStatus.textContent = lastSnapshotAt ? `Last update ${lastSnapshotAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}` : "State is not available yet";
    } finally {
      polling = false;
      ui.refresh.removeAttribute("aria-busy");
    }
  }

  async function revealAuthorization() {
    if (revealedValue && revealedValue.requestId === selectedId) {
      hideRevealedAuthorization();
      return;
    }
    const requestId = selectedId;
    const latestAtReveal = lastLatestId;
    const request = requestId ? requestsById.get(requestId) : null;
    if (!request || !request.authentication || request.authentication.canReveal !== true || !requestId) return;
    ui.reveal.disabled = true;
    ui.reveal.textContent = "Requesting…";
    try {
      const response = await fetch("/cockpit/reveal", {
        method: "POST", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-Demo-Cockpit": "1" },
        body: JSON.stringify({ requestId }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok || typeof result.authorization !== "string") throw new Error("Credential unavailable");
      if (selectedId !== requestId || lastLatestId !== latestAtReveal || document.hidden || !requestsById.has(requestId)) return;
      const value = result.authorization;
      // Retain only in memory for the short explicit reveal window. Never log it.
      revealedValue = { requestId, value };
      ui.authValue.textContent = value;
      ui.authHelp.textContent = "Visible for this request. It will hide automatically in 15 seconds or when the selection changes.";
      ui.reveal.textContent = "Hide credential";
      if (revealTimer) window.clearTimeout(revealTimer);
      revealTimer = window.setTimeout(() => hideRevealedAuthorization(), 15000);
    } catch {
      if (selectedId === requestId) {
        revealedValue = null;
        ui.authHelp.textContent = "The known demo credential could not be revealed for this request.";
        ui.authValue.textContent = "Bearer ••••••••";
        ui.reveal.textContent = "Reveal demo key";
      }
    } finally {
      ui.reveal.disabled = false;
      renderSelectedRequest();
    }
  }

  async function submitQuote(event) {
    event.preventDefault();
    if (quotePending) return;
    const subtotal = Number(ui.subtotal.value);
    const shipping = Number(ui.shipping.value);
    const valid = (value) => Number.isInteger(value) && value >= 0 && value <= 1000000;
    if (!valid(subtotal) || !valid(shipping)) {
      ui.quoteOutcome.textContent = "Check the input values";
      ui.quoteFeedback.textContent = "Enter whole numbers from 0 to 1,000,000.";
      (valid(subtotal) ? ui.shipping : ui.subtotal).focus();
      return;
    }
    quotePending = true;
    ui.quoteSubmit.disabled = true;
    ui.quoteSubmit.textContent = "Sending…";
    ui.quoteOutcome.textContent = "Quote request in progress";
    ui.quoteTotal.textContent = "—";
    ui.quoteFeedback.textContent = "Waiting for the prepared workflow response.";
    try {
      const response = await fetch("/cockpit/quote", {
        method: "POST", cache: "no-store",
        headers: { "Content-Type": "application/json", "X-Demo-Cockpit": "1" },
        body: JSON.stringify({ subtotal, shipping }),
      });
      const result = await response.json().catch(() => null);
      const observed = result && typeof result === "object" ? { ...result } : { status: response.status, request: { subtotal, shipping }, body: null };
      if (observed.status === null || observed.status === undefined) observed.status = response.status;
      if (!observed.request) observed.request = { subtotal, shipping };
      if (observed.body === undefined && !response.ok) observed.body = result;
      renderQuote(observed);
      if (result === null) ui.quoteFeedback.textContent = `The endpoint returned HTTP ${response.status} without a JSON response.`;
    } catch {
      ui.quoteOutcome.textContent = "Quote request failed";
      ui.quoteOutcome.style.color = "var(--bad)";
      ui.quoteTotal.textContent = "—";
      ui.quoteFeedback.textContent = "No response was received from the prepared workflow.";
    } finally {
      quotePending = false;
      ui.quoteSubmit.disabled = false;
      ui.quoteSubmit.textContent = "Send quote request";
    }
  }

  ui.refresh.addEventListener("click", () => {
    ui.refresh.setAttribute("aria-busy", "true");
    pollState();
  });
  ui.reveal.addEventListener("click", revealAuthorization);
  ui.quoteForm.addEventListener("submit", submitQuote);
  document.addEventListener("visibilitychange", () => {
    if (document.hidden && revealedValue) hideRevealedAuthorization();
  });

  pollState();
  window.setInterval(pollState, 1000);
})();
