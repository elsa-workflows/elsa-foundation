# Secrets master-key rotation

## Why rotate

Elsa encrypts stored secret values (AES-GCM) with a master key. You rotate that key when it may have leaked, when a person who knew it leaves, or simply on a schedule. Rotation must not make existing secrets unreadable.

Elsa makes that possible with a **key-ring**: a set of keys where exactly one is *active* for encrypting new values, and every key in the ring can still *decrypt*. Each stored value records which key encrypted it.

## Rotation in four steps

1. **Add a new key** to `Keys` with a fresh `KeyId`. Leave the existing keys in place.
2. **Point `ActiveKeyId`** at the new key.
3. **Restart** the host. New writes are encrypted under the new key (`v2:<newKeyId>:...`); existing
   values remain readable because their original key is still in the ring.
4. **(Optional) lazily re-encrypt.** Rotating a secret (or any write) re-encrypts its value under
   the active key. Once every value that used an old key has been re-written, you may remove the old
   key from `Keys`.

> **Do not remove a key** while any stored value was encrypted under it — that value would become
> permanently unreadable. Keep old keys in the ring until you are sure nothing depends on them.

The rest of this page is the reference behind those steps.

## Payload format

Protected values are stored in a colon-delimited format:

| Format | Meaning |
|---|---|
| `v2:<keyId>:<nonce>:<tag>:<ciphertext>` | Current format. The `keyId` records which key encrypted the value, so it can still be decrypted after the active key changes. |
| `v1:<nonce>:<tag>:<ciphertext>` | Legacy format, written before the key-ring existed. Read with the reserved `legacy` key derived from `Elsa:Secrets:EncryptionKey`. |

## Configuration

### Shell feature settings

A shell supplies its keys as settings of the `Secrets` feature. `Keys` maps a key id to its key material.

```jsonc
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "Secrets": {
            // Single key. When set it is added to the ring under the reserved id "legacy".
            "EncryptionKey": "old-master-key",

            // The key-ring: key id to key material.
            "Keys": {
              "2026-01": "first-rotation-key",
              "2026-07": "second-rotation-key"
            },

            // The key new values are encrypted with. Must reference a key in the ring (or "legacy").
            "ActiveKeyId": "2026-07"
          }
        }
      }
    }
  }
}
```

`EncryptionKey` and `Keys` are declared secret, so the feature catalog masks them. Supply them through a secret
store or environment variables outside development, never in committed configuration.

`Elsa.Workbench` commits a development-only `EncryptionKey` in `shells.json` so a fresh checkout can store secrets.
`shells.Production.json` blanks it, so a Production host has no key until one is supplied, for example as
`CShells__Shells__default__Features__Secrets__EncryptionKey`.

A setting that is left unset is not applied, so a host that configures `SecretsOptions` itself keeps its keys when
the shell sets none. A key id declared by both the host and the shell is refused as a duplicate.

### Host options

A host that composes the module with `AddSecrets()` instead of the feature configures `SecretsOptions` itself, for
example with `services.Configure<SecretsOptions>(configuration.GetSection("Elsa:Secrets"))`. `AddSecrets(configuration)`
on its own reads only `Elsa:Secrets:EncryptionKey`. Here `Keys` is a list:

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
