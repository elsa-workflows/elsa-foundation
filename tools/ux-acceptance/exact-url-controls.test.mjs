import { test } from "node:test";
import assert from "node:assert/strict";
import { hasExactUrlMultimap } from "../../Studio/tests/acceptance/exactUrl.mjs";

// Tiny non-browser causal controls; no host, Playwright, product runtime or network.
const controls = {
  "positive": [
    {
      "name": "original backend-first to backend-last",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "actual": "https://studio.example/workflows/definitions?definition=definition-123&version=version-456&backend=legacy"
    },
    {
      "name": "different key positions preserve duplicate value order",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&scope=first&scope=second",
      "actual": "https://studio.example/workflows/definitions?scope=first&backend=legacy&scope=second&version=version-456&definition=definition-123"
    }
  ],
  "negative": [
    {
      "name": "wrong backend",
      "actual": "https://studio.example/workflows/definitions?backend=wrong&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "missing backend",
      "actual": "https://studio.example/workflows/definitions?definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "empty backend",
      "actual": "https://studio.example/workflows/definitions?backend=&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "duplicate backend",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&backend=legacy",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "wrong definition",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=wrong&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "missing definition",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "empty definition",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "duplicate definition",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&definition=definition-123",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "wrong version",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=wrong",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "missing version",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "empty version",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "duplicate version",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "extra unknown key",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=value",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "missing expected unknown key",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=one"
    },
    {
      "name": "wrong expected unknown value",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=two",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=one"
    },
    {
      "name": "duplicate expected unknown key",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=one&unknown=one",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&unknown=one"
    },
    {
      "name": "duplicate value order changed",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&scope=second&scope=first",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456&scope=first&scope=second"
    },
    {
      "name": "wrong scheme",
      "actual": "http://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "wrong host",
      "actual": "https://other.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "wrong port",
      "actual": "https://studio.example:8443/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "wrong path",
      "actual": "https://studio.example/workflows/executables?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "extra fragment",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456#unexpected",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "missing expected fragment",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456#expected"
    },
    {
      "name": "wrong fragment",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456#wrong",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456#expected"
    },
    {
      "name": "unexpected userinfo",
      "actual": "https://reader@studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "invalid actual",
      "actual": "not an absolute URL",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    },
    {
      "name": "invalid expected",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456",
      "expected": "not an absolute URL"
    },
    {
      "name": "extra empty fragment marker",
      "actual": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456#",
      "expected": "https://studio.example/workflows/definitions?backend=legacy&definition=definition-123&version=version-456"
    }
  ]
};
for (const row of controls.positive) test(row.name, () => {
  assert.equal(hasExactUrlMultimap(row.actual, row.expected), true);
  assert.equal(hasExactUrlMultimap(new URL(row.actual), new URL(row.expected)), true);
});
for (const row of controls.negative) test(row.name, () => {
  assert.equal(hasExactUrlMultimap(row.actual, row.expected), false);
});
