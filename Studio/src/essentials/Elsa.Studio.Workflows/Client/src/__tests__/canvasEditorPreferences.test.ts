import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { createCanvasEditorPreferences } from "../graph-authoring/canvasEditorPreferences";

const missing = Object.assign(new Error("Not composed"), { status: 404 });
function api(subject = "user", tenantId = "tenant", hostId = "studio", baseUrl = "https://backend.example") {
  return { backend: { baseUrl, http: { getJson: vi.fn(async () => { throw missing; }), putJson: vi.fn(async () => { throw missing; }) } },
    runtime: { hostId, identity: { subject, tenantId } } } as unknown as Pick<ElsaStudioModuleApi, "backend" | "runtime">;
}
const document = (editorId: string, revision = "rev-1") => ({ namespace: "workflow-authoring", schemaVersion: 1, revision, value: { editorId } });

describe("canvas editor saved preference", () => {
  beforeEach(() => localStorage.clear());
  it("falls back for an absent endpoint, isolates user/tenant/host/backend and keeps unavailable editor ids", async () => {
    const initial = api();
    await createCanvasEditorPreferences(initial).save("uninstalled.editor");
    expect(await createCanvasEditorPreferences(api()).load()).toBe("uninstalled.editor");
    for (const other of [api("other"), api("user", "other"), api("user", "tenant", "other"), api("user", "tenant", "studio", "https://other.example")])
      expect(await createCanvasEditorPreferences(other).load()).toBeNull();
    expect(initial.backend.http.putJson).toHaveBeenCalledWith("/_elsa/studio/preferences/workflow-authoring",
      { schemaVersion: 1, value: { editorId: "uninstalled.editor" } }, { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-None-Match": "*" } });
  });

  it("keeps anonymous preferences on the device without invoking HTTP", async () => {
    const anonymous = api("anonymous");
    const store = createCanvasEditorPreferences(anonymous);
    const write = store.save("A".repeat(128));
    expect(await store.load()).toBe("A".repeat(128));
    await write;
    expect(anonymous.backend.http.putJson).not.toHaveBeenCalled();
    expect(anonymous.backend.http.getJson).not.toHaveBeenCalled();
  });

  it.each(["read", "write"])("refreshes the device fallback after an accepted server %s", async operation => {
    const current = api();
    const store = createCanvasEditorPreferences(current);
    await store.save("offline-A");
    if (operation === "write") {
      current.backend.http.putJson = vi.fn().mockResolvedValue(document("online-B"));
      await store.save("online-B");
    } else {
      current.backend.http.getJson = vi.fn().mockResolvedValue(document("online-B"));
      expect(await store.load()).toBe("online-B");
    }
    const disconnected = api();
    disconnected.backend.http.getJson = vi.fn().mockRejectedValue(new Error("Offline again"));
    expect(await createCanvasEditorPreferences(disconnected).load()).toBe("online-B");
  });

  it.each(["", "x".repeat(129), "a/b", "with space", "é", "line\n"])("refuses invalid id %j before any write", async id => {
    const current = api();
    await expect(createCanvasEditorPreferences(current).save(id)).rejects.toThrow("Invalid");
    expect(current.backend.http.putJson).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
  });

  it.each([null, { schemaVersion: 2, value: { editorId: "x" } }, { schemaVersion: 1, value: { editorId: "x", extra: true } },
    { schemaVersion: 1, value: { editorId: "a/b" } }, { schemaVersion: 1, value: [] }])("refuses invalid or newer local values %j", async invalid => {
    const store = createCanvasEditorPreferences(api("anonymous"));
    await store.save("example");
    localStorage.setItem(localStorage.key(0)!, JSON.stringify(invalid));
    expect(await store.load()).toBeNull();
  });

  it("serializes writes and uses the preceding successful revision", async () => {
    const current = api();
    let release!: (value: unknown) => void;
    const firstResponse = new Promise<unknown>(resolve => { release = resolve; });
    const put = vi.fn().mockReturnValueOnce(firstResponse).mockResolvedValueOnce(document("second", "rev-2"));
    current.backend.http.putJson = put;
    const store = createCanvasEditorPreferences(current);
    const first = store.save("first"), second = store.save("second");
    await Promise.resolve();
    expect(put).toHaveBeenCalledTimes(1);
    release(document("first"));
    await Promise.all([first, second]);
    expect(put.mock.calls[1][2].headers).toEqual({ "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"rev-1"' });
  });

  it.each([409, 412, 403])("propagates status %s without local overrides and keeps the queue usable", async status => {
    const current = api();
    const conflict = Object.assign(new Error("Refused"), { status });
    current.backend.http.getJson = vi.fn().mockResolvedValue(document("saved"));
    let releaseRetry!: (value: unknown) => void;
    const retryResponse = new Promise(resolve => { releaseRetry = resolve; });
    const put = vi.fn().mockRejectedValueOnce(conflict).mockReturnValueOnce(retryResponse);
    current.backend.http.putJson = put;
    const store = createCanvasEditorPreferences(current);
    expect(await store.load()).toBe("saved");
    const acceptedCache = localStorage.getItem(localStorage.key(0)!);
    const refused = store.save("conflicting"), retry = store.save("retry");
    await expect(refused).rejects.toBe(conflict);
    expect(localStorage.getItem(localStorage.key(0)!)).toBe(acceptedCache);
    releaseRetry(document("retry", "rev-2"));
    await retry;
    expect(put.mock.calls.map(call => call[2].headers["If-Match"])).toEqual(['"rev-1"', '"rev-1"']);
    expect(JSON.parse(localStorage.getItem(localStorage.key(0)!)!).value.editorId).toBe("retry");
  });

  it("queues a write behind an in-flight read instead of sending an obsolete precondition", async () => {
    const current = api();
    let release!: (value: unknown) => void;
    current.backend.http.getJson = vi.fn().mockReturnValue(new Promise(resolve => { release = resolve; }));
    const put = vi.fn().mockResolvedValue(document("chosen", "rev-2"));
    current.backend.http.putJson = put;
    const store = createCanvasEditorPreferences(current);
    const read = store.load(), write = store.save("chosen");
    await Promise.resolve();
    expect(put).not.toHaveBeenCalled();
    release(document("saved"));
    await Promise.all([read, write]);
    expect(put.mock.calls[0][2].headers["If-Match"]).toBe('"rev-1"');
  });

  it("rejects a mismatched backend document before adopting its revision", async () => {
    const current = api();
    current.backend.http.getJson = vi.fn().mockResolvedValue({ ...document("saved", "poisoned"), namespace: "another" });
    const put = vi.fn().mockResolvedValue(document("chosen"));
    current.backend.http.putJson = put;
    const store = createCanvasEditorPreferences(current);
    expect(await store.load()).toBeNull();
    await store.save("chosen");
    expect(put.mock.calls[0][2].headers["If-None-Match"]).toBe("*");
  });

  it("retains its known conditional revision across a temporary read outage", async () => {
    const current = api();
    current.backend.http.getJson = vi.fn().mockResolvedValueOnce(document("saved")).mockRejectedValueOnce(new Error("Offline"));
    const put = vi.fn().mockResolvedValue(document("chosen", "rev-2"));
    current.backend.http.putJson = put;
    const store = createCanvasEditorPreferences(current);
    expect(await store.load()).toBe("saved");
    await store.load();
    await store.save("chosen");
    expect(put.mock.calls[0][2].headers["If-Match"]).toBe('"rev-1"');
  });

  it("does not silently accept an incompatible successful write response", async () => {
    const current = api();
    current.backend.http.putJson = vi.fn().mockResolvedValue({ ...document("chosen"), schemaVersion: 2 });
    await expect(createCanvasEditorPreferences(current).save("chosen")).rejects.toThrow("incompatible");
    expect(localStorage.length).toBe(0);
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((finish, fail) => { resolve = finish; reject = fail; });
  return { promise, resolve, reject };
}

describe("canvas editor preference ordering across stores", () => {
  beforeEach(() => localStorage.clear());

  it("finishes an earlier store GET before a recreated store reads and saves the latest recovery choice", async () => {
    const earlier = api(), current = api();
    const response = deferred<unknown>(), started = deferred<void>();
    earlier.backend.http.getJson = vi.fn().mockImplementation(() => { started.resolve(); return response.promise; });
    current.backend.http.getJson = vi.fn().mockResolvedValue(document("before", "r1"));
    current.backend.http.putJson = vi.fn().mockResolvedValue(document("chosen", "r2"));
    const oldRead = createCanvasEditorPreferences(earlier).load();
    await started.promise;
    const store = createCanvasEditorPreferences(current), read = store.load();
    await Promise.resolve();
    const concurrentReads = vi.mocked(current.backend.http.getJson).mock.calls.length;
    response.resolve(document("before", "r1"));
    expect(await oldRead).toBe("before");
    expect(await read).toBe("before");
    await store.save("chosen");
    expect(concurrentReads).toBe(0);
    expect(current.backend.http.putJson).toHaveBeenCalledWith(expect.any(String), expect.anything(),
      { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"r1"' } });
    expect(await createCanvasEditorPreferences(api()).load()).toBe("chosen");
  });

  it("finishes an accepted earlier store PUT response before another store reads r2 and saves r3", async () => {
    const earlier = api(), current = api();
    let server = document("before", "r1");
    earlier.backend.http.getJson = vi.fn().mockImplementation(async () => server);
    current.backend.http.getJson = vi.fn().mockImplementation(async () => server);
    const response = deferred<unknown>(), accepted = deferred<void>();
    earlier.backend.http.putJson = vi.fn().mockImplementation(async (_path, _body, options) => {
      expect(options?.headers).toMatchObject({ "If-Match": '"r1"' });
      server = document("first", "r2");
      accepted.resolve();
      return response.promise;
    });
    current.backend.http.putJson = vi.fn().mockImplementation(async (_path, _body, options) => {
      expect(options?.headers).toMatchObject({ "If-Match": '"r2"' });
      server = document("chosen", "r3");
      return server;
    });
    const oldStore = createCanvasEditorPreferences(earlier);
    expect(await oldStore.load()).toBe("before");
    const oldWrite = oldStore.save("first");
    await accepted.promise;
    const store = createCanvasEditorPreferences(current), read = store.load();
    await Promise.resolve();
    const concurrentReads = vi.mocked(current.backend.http.getJson).mock.calls.length;
    response.resolve(server);
    await oldWrite;
    expect(await read).toBe("first");
    await store.save("chosen");
    expect(concurrentReads).toBe(0);
    expect(server).toEqual(document("chosen", "r3"));
    expect(await createCanvasEditorPreferences(api()).load()).toBe("chosen");
  });

  it("keeps a pending successor ordered when an older scope operation settles", async () => {
    const firstApi = api(), secondApi = api(), thirdApi = api();
    const firstResponse = deferred<unknown>(), firstStarted = deferred<void>();
    const secondResponse = deferred<unknown>(), secondStarted = deferred<void>();
    firstApi.backend.http.getJson = vi.fn().mockImplementation(() => { firstStarted.resolve(); return firstResponse.promise; });
    secondApi.backend.http.getJson = vi.fn().mockImplementation(() => { secondStarted.resolve(); return secondResponse.promise; });
    thirdApi.backend.http.getJson = vi.fn().mockResolvedValue(document("saved", "r1"));
    thirdApi.backend.http.putJson = vi.fn().mockResolvedValue(document("chosen", "r2"));
    const firstRead = createCanvasEditorPreferences(firstApi).load();
    await firstStarted.promise;
    const secondRead = createCanvasEditorPreferences(secondApi).load();
    firstResponse.resolve(document("saved", "r1"));
    await firstRead;
    await secondStarted.promise;
    const store = createCanvasEditorPreferences(thirdApi), thirdRead = store.load();
    await Promise.resolve();
    const concurrentReads = vi.mocked(thirdApi.backend.http.getJson).mock.calls.length;
    secondResponse.resolve(document("saved", "r1"));
    await secondRead;
    expect(await thirdRead).toBe("saved");
    await store.save("chosen");
    expect(concurrentReads).toBe(0);
    expect(await createCanvasEditorPreferences(api()).load()).toBe("chosen");
  });

  it.each([
    ["subject", () => api("another-user")],
    ["tenant", () => api("user", "another-tenant")],
    ["host", () => api("user", "tenant", "another-host")],
    ["backend", () => api("user", "tenant", "studio", "https://other.example")]
  ] as const)("lets a different %s scope complete while an earlier scope is pending", async (_scope, differentApi) => {
    const earlier = api(), current = differentApi();
    const response = deferred<unknown>(), started = deferred<void>();
    earlier.backend.http.getJson = vi.fn().mockImplementation(() => { started.resolve(); return response.promise; });
    current.backend.http.getJson = vi.fn().mockResolvedValue(document("before", "r1"));
    current.backend.http.putJson = vi.fn().mockResolvedValue(document("chosen", "r2"));
    const oldRead = createCanvasEditorPreferences(earlier).load();
    await started.promise;
    try {
      const store = createCanvasEditorPreferences(current);
      expect(await store.load()).toBe("before");
      await store.save("chosen");
      expect(current.backend.http.putJson).toHaveBeenCalledTimes(1);
    } finally {
      response.resolve(document("earlier", "r1"));
      await oldRead;
    }
    expect(await createCanvasEditorPreferences(differentApi()).load()).toBe("chosen");
    expect(await createCanvasEditorPreferences(api()).load()).toBe("earlier");
  });

  it.each([403, 409, 412])("recovers another queued store after a refused %s write without sharing its CAS revision", async status => {
    const earlier = api(), current = api();
    earlier.backend.http.getJson = vi.fn().mockResolvedValue(document("saved", "r1"));
    current.backend.http.getJson = vi.fn().mockResolvedValue(document("saved", "r1"));
    current.backend.http.putJson = vi.fn().mockResolvedValue(document("chosen", "r2"));
    const response = deferred<unknown>(), started = deferred<void>();
    earlier.backend.http.putJson = vi.fn().mockImplementation(() => { started.resolve(); return response.promise; });
    const oldStore = createCanvasEditorPreferences(earlier);
    await oldStore.load();
    const oldWrite = oldStore.save("refused");
    const refused = expect(oldWrite).rejects.toMatchObject({ status });
    await started.promise;
    const store = createCanvasEditorPreferences(current), read = store.load();
    await Promise.resolve();
    const concurrentReads = vi.mocked(current.backend.http.getJson).mock.calls.length;
    response.reject(Object.assign(new Error("Refused"), { status }));
    await refused;
    expect(await read).toBe("saved");
    expect(await createCanvasEditorPreferences(api()).load()).toBe("saved");
    await store.save("chosen");
    expect(concurrentReads).toBe(0);
    expect(current.backend.http.putJson).toHaveBeenCalledWith(expect.any(String), expect.anything(),
      { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"r1"' } });
    expect(await createCanvasEditorPreferences(api()).load()).toBe("chosen");
    earlier.backend.http.putJson = vi.fn().mockRejectedValue(Object.assign(new Error("Stale"), { status: 412 }));
    await expect(oldStore.save("stale")).rejects.toMatchObject({ status: 412 });
    expect(earlier.backend.http.putJson).toHaveBeenCalledWith(expect.any(String), expect.anything(),
      { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"r1"' } });
    expect(await createCanvasEditorPreferences(api()).load()).toBe("chosen");
    earlier.backend.http.getJson = vi.fn().mockResolvedValue(document("chosen", "r2"));
    expect(await oldStore.load()).toBe("chosen");
    earlier.backend.http.putJson = vi.fn().mockResolvedValue(document("recovered", "r3"));
    await oldStore.save("recovered");
    expect(earlier.backend.http.putJson).toHaveBeenCalledWith(expect.any(String), expect.anything(),
      { headers: { "X-Elsa-Studio-Host-Id": "studio", "If-Match": '"r2"' } });
    expect(await createCanvasEditorPreferences(api()).load()).toBe("recovered");
  });
});
