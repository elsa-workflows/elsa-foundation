import { describe, expect, it } from "vitest";
import { sha256, sha256Hex } from "../sha256";
import { createDraftSnapshotId, getDraftContentRevision, getDraftRevision } from "../workflow-editor/editorHelpers";
import { pageRevision } from "../workflow-editor/workflowLibraryWeaver";
import type { WorkflowDraft } from "../workflowTypes";

// FIPS 180-4 example vectors (NIST CSRC "SHA-256" examples and the FIPS 180-2 long-message vector).
const vectors: Array<[string, string]> = [
  ["", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"],
  ["abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"],
  ["abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"],
  ["abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu",
    "cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1"]
];

describe("module-owned SHA-256", () => {
  it.each(vectors)("matches the NIST vector for %j", (message, digest) => {
    expect(sha256Hex(message)).toBe(digest);
  });

  it("matches the one-million-'a' NIST vector across many blocks", () => {
    expect(sha256Hex("a".repeat(1_000_000))).toBe("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
  });

  it("hashes the UTF-8 encoding, not UTF-16 code units", () => {
    const hex = (bytes: Uint8Array) => Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
    // "é" is U+00E9: one UTF-16 code unit, two UTF-8 bytes (0xC3 0xA9).
    expect(sha256Hex("é")).toBe(hex(sha256(Uint8Array.of(0xc3, 0xa9))));
    expect(sha256Hex("é")).not.toBe(hex(sha256(Uint8Array.of(0xe9))));
  });

  it("uses the shared JSON hash for content revisions and change detection", () => {
    const draft: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: null }, layout: [], validationErrors: [] };
    const moved: WorkflowDraft = { ...draft, layout: [{ nodeId: "a", x: 1, y: 2 }] };
    expect(getDraftContentRevision(draft)).toMatch(/^sha256:[0-9a-f]{64}$/);
    expect(getDraftContentRevision(draft)).toBe(getDraftContentRevision(structuredClone(draft)));
    expect(getDraftContentRevision(moved)).not.toBe(getDraftContentRevision(draft));
    expect(getDraftRevision(draft)).toMatch(/^[0-9a-f]{64}$/);
    const freeze = "First-stable hash freeze: changes require an explicit identity/migration decision.";
    expect(getDraftRevision(draft), freeze).toBe("02dd215eb5414c3301f89cc478313efec4fc176c1dbe394be1d633a8aaec0a0b");
    expect(getDraftContentRevision(draft), freeze).toBe("sha256:02dd215eb5414c3301f89cc478313efec4fc176c1dbe394be1d633a8aaec0a0b");
    expect(createDraftSnapshotId(draft), freeze).toBe("draft-73f3d6ce46030f48f6941aab312a854e69b895198ae3a433c5427e08a651f3b5");
    expect(createDraftSnapshotId(moved)).toBe(createDraftSnapshotId(draft));
    expect(createDraftSnapshotId({ ...draft, state: { rootActivity: null, workflowActivityOptions: { changed: true } } })).not.toBe(createDraftSnapshotId(draft));
  });

  it("pins the page revision projection and preserves array order and null presence", () => {
    const body = { rows: [{ id: "a", count: 1 }], selection: [] };
    expect(pageRevision(body), "First-stable hash freeze: changes require an explicit identity/migration decision.")
      .toBe("sha256:12a5044a0af9b760d49e7366683a07217e3166d0c2fbf4dccca07b0963ad53a8");
    expect(pageRevision({ selection: [], rows: [{ count: 1, id: "a" }] })).toBe(pageRevision(body));
    expect(pageRevision({ ...body, selection: null })).not.toBe(pageRevision(body));
    expect(pageRevision([1, 2])).not.toBe(pageRevision([2, 1]));
  });
});
