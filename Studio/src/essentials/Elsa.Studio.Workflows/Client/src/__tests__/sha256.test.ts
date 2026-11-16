import { describe, expect, it } from "vitest";
import { sha256, sha256Hex } from "../sha256";
import { getDraftContentRevision, getDraftRevision } from "../workflow-editor/editorHelpers";
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

  it("gives every working state a collision-resistant content revision, while the 32-bit hash stays for change detection", () => {
    const draft: WorkflowDraft = { id: "draft", definitionId: "definition", state: { rootActivity: null }, layout: [], validationErrors: [] };
    const moved: WorkflowDraft = { ...draft, layout: [{ nodeId: "a", x: 1, y: 2 }] };
    expect(getDraftContentRevision(draft)).toMatch(/^sha256:[0-9a-f]{64}$/);
    expect(getDraftContentRevision(draft)).toBe(getDraftContentRevision(structuredClone(draft)));
    expect(getDraftContentRevision(moved)).not.toBe(getDraftContentRevision(draft));
    expect(getDraftRevision(draft)).toMatch(/^[0-9a-f]{8}$/);
  });
});
