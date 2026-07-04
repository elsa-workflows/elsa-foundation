# Secrets master-key rotation

Elsa protects secret values with AES-GCM using a **key-ring**: a set of symmetric keys where
exactly one key is *active* for encryption, while every key in the ring can be used for
*decryption*. This lets you rotate the encryption key without losing access to values that were
written under an older key.

## Payload format

Protected values are stored in a colon-delimited format:

- `v2:<keyId>:<nonce>:<tag>:<ciphertext>` — current format. The `keyId` records exactly which key
  encrypted the value, so it can still be decrypted after the active key changes.
- `v1:<nonce>:<tag>:<ciphertext>` — legacy format written before the key-ring existed. It is read
  using the reserved `legacy` key derived from `Elsa:Secrets:EncryptionKey`.

## Configuration

```jsonc
{
  "Elsa": {
    "Secrets": {
      // Legacy single key. When set it is added to the ring under the reserved id "legacy".
      "EncryptionKey": "old-master-key",

      // The key-ring. Each key has a stable id and its own material.
      "Keys": [
        { "KeyId": "2026-01", "Key": "first-rotation-key" },
        { "KeyId": "2026-07", "Key": "second-rotation-key" }
      ],

      // The key new values are encrypted with. Must reference a key in the ring (or "legacy").
      "ActiveKeyId": "2026-07"
    }
  }
}
```

### Key id rules (validated at startup)

Key ids are embedded in the colon-delimited payload, so they are validated when the key-ring is
built — misconfiguration fails fast at startup rather than at the first encrypt:

- A key id must be **non-empty** and **not whitespace**.
- Allowed characters are ASCII letters, digits, `.`, `-` and `_`. In particular `:` is rejected.
- **Duplicate** key ids are rejected.
- `ActiveKeyId`, when set, **must** reference a key that is present in the ring.
- `legacy` is **reserved** for `EncryptionKey` and cannot also be declared in `Keys`.

If no `ActiveKeyId` is set, the active key is the `legacy` key when present, otherwise the single
configured key. With multiple keys and no `ActiveKeyId`, startup fails and asks you to choose one.

## Rotation procedure

1. **Add a new key** to `Keys` with a fresh `KeyId`. Leave the existing keys in place.
2. **Point `ActiveKeyId`** at the new key.
3. **Restart** the host. New writes are encrypted under the new key (`v2:<newKeyId>:...`); existing
   values remain readable because their original key is still in the ring.
4. **(Optional) lazily re-encrypt.** Rotating a secret (or any write) re-encrypts its value under
   the active key. Once every value that used an old key has been re-written, you may remove the old
   key from `Keys`.

> **Do not remove a key** while any stored value was encrypted under it — that value would become
> permanently unreadable. Keep old keys in the ring until you are sure nothing depends on them.
