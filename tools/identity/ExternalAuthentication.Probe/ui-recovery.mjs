// Owned composed-host proof only. All mutations use real public APIs/UI and narrowly armed probe faults.
export async function prepareInvalidDraft({ page, expect, capture, setMode }) {
  await page.getByLabel("Display name", { exact: true }).fill("");
  await page.setViewportSize({ width: 360, height: 844 }); await setMode("dark");
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(page.getByLabel("Display name", { exact: true })).toBeFocused();
  expect(await page.getByLabel("Display name", { exact: true }).evaluate(input => input.validity.valueMissing)).toBe(true);
  await capture(page, "missing-name-narrow-dark", { action: "submit-missing-name" });
  await setMode("light"); await page.setViewportSize({ width: 1440, height: 1100 });
  await page.getByLabel("Display name", { exact: true }).fill("Studio company");
  await page.getByLabel("Connection key", { exact: true }).fill("invalid key");
  await page.getByRole("button", { name: "Save draft", exact: true }).click();
  await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("lowercase");
  await capture(page, "invalid-key", { action: "submit-invalid-key" });
  await page.getByLabel("Connection key", { exact: true }).fill("studio-company");
  const issuer = await page.getByLabel("Issuer URL", { exact: true }).inputValue();
  await page.getByLabel("Issuer URL", { exact: true }).fill(issuer + "/missing-fixture");
}

export async function recoverFailedTest({ page, expect, capture, metadata, confirm }) {
  await page.getByRole("button", { name: "Test configuration", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Current configuration needs attention", exact: true })).toBeVisible();
  await expect(page.getByText("Configuration test passed", { exact: true })).toHaveCount(0);
  await capture(page, "configuration-test-failed", { action: "test-owned-missing-discovery" });
  await page.getByLabel("Issuer URL", { exact: true }).fill(metadata.CompanyIssuer);
  await page.getByRole("button", { name: "Save changes", exact: true }).click(); await confirm(page, "Save changes");
  // Reopen by keyboard before the ordinary successful test/preview to retain those original assertions.
  await page.locator(".external-auth-row").filter({ has: page.getByText("Company", { exact: true }) }).click();
  const row = page.locator(".external-auth-row").filter({ has: page.getByText("Studio company", { exact: true }) });
  await expect(row).toBeEnabled(); await row.focus(); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Studio company", exact: true })).toBeVisible();
  await expect(page.getByLabel("Connection key", { exact: true })).toHaveValue("studio-company");
  await expect(page.getByLabel("Issuer URL", { exact: true })).toHaveValue(metadata.CompanyIssuer);
  await capture(page, "corrected-draft-enter-reopen", { action: "enter-reopen-corrected-draft" });
}

export async function exercisePreviewDenial({ page, admin, backend, expect, capture, confirm, control, exchangeCount }) {
  const beforeExchanges = await exchangeCount(); const before = await (await admin.request.get(backend + "/__probe/state")).json();
  const beforeSession = await (await admin.request.get(backend + "/_elsa/identity/session")).json();
  await control("deny-preview");
  await page.getByRole("button", { name: "Preview sign-in", exact: true }).click(); await confirm(page, "Preview provider sign-in", "Continue to provider");
  await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("provider sign-in was cancelled");
  await expect.poll(() => [...new URL(page.url()).searchParams.keys()].some(key => key.startsWith("externalPreview"))).toBe(false);
  await capture(page, "preview-provider-denied", { action: "owned-provider-denial-return" });
  const exchanges = await exchangeCount() - beforeExchanges; const after = await (await admin.request.get(backend + "/__probe/state")).json();
  for (const name of ["users", "links", "sessions", "tokenRequests"]) expect(after[name]).toBe(before[name]);
  expect(after.tokens).toBe(before.tokens + exchanges * 2);
  const afterSession = await (await admin.request.get(backend + "/_elsa/identity/session")).json();
  for (const name of ["subject", "roles", "permissions"]) expect(afterSession[name]).toEqual(beforeSession[name]);
}

export async function exerciseConnectionConflict({ page, admin, componentUrl, expect, capture, confirm }) {
  await page.getByLabel("Display name", { exact: true }).fill("Studio company edited");
  // Assess against the current revision first; the other window commits while this confirmation stays open.
  await page.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Save changes", exact: true })).toBeVisible();
  const second = await admin.newPage();
  try {
    await second.goto(componentUrl("connections"));
    await second.locator(".external-auth-row").filter({ has: second.getByText("Studio company", { exact: true }) }).click();
    await second.getByLabel("Display order", { exact: true }).fill("7");
    await second.getByRole("button", { name: "Save changes", exact: true }).click(); await confirm(second, "Save changes");
    await expect(second.getByLabel("Display order", { exact: true })).toHaveValue("7"); await second.waitForLoadState("networkidle");
  } finally { await second.close(); }
  const response = page.waitForResponse(result => result.request().method() === "POST" && new URL(result.url()).pathname.endsWith("/_elsa/identity/external-connections/commands"));
  await confirm(page, "Save changes");
  expect((await response).status()).toBe(409);
  await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("changed on the server");
  await expect(page.getByLabel("Display name", { exact: true })).toHaveValue("Studio company edited");
  await capture(page, "stale-draft-preserved", { action: "real-catalog-cas-conflict" });
  const refresh = page.getByRole("button", { name: "Refresh connections", exact: true });
  await refresh.click();
  const discard = page.getByRole("dialog", { name: "Discard unsaved changes?", exact: true });
  await expect(discard).toBeVisible();
  await capture(page, "discard-conflicting-draft", { region: '[role="dialog"]', action: "request-draft-discard" });
  await discard.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(refresh).toBeFocused(); await expect(page.getByLabel("Display name", { exact: true })).toHaveValue("Studio company edited");
  await capture(page, "discard-cancel-preserves-draft", { action: "cancel-discard-focus-return" });
  await refresh.click(); await confirm(page, "Discard unsaved changes?", "Discard changes");
  await page.locator(".external-auth-row").filter({ has: page.getByText("Studio company", { exact: true }) }).click();
  await page.getByLabel("Display name", { exact: true }).fill("Studio company edited");
  await page.getByRole("button", { name: "Save changes", exact: true }).click(); await confirm(page, "Save changes");
  await page.locator(".external-auth-row").filter({ has: page.getByText("Company", { exact: true }) }).click();
  const row = page.locator(".external-auth-row").filter({ has: page.getByText("Studio company edited", { exact: true }) });
  await expect(row).toBeEnabled(); await row.focus(); await page.keyboard.press("Enter");
  await expect(page.getByLabel("Display name", { exact: true })).toHaveValue("Studio company edited");
  await expect(page.getByLabel("Display order", { exact: true })).toHaveValue("7");
  await capture(page, "conflict-recovered-save-reopen", { action: "save-current-revision-reopen" });
}

export async function exerciseAccessRecovery({ page, admin, browser, backend, componentUrl, target, partnerSession, subject, expect, capture, confirm, control, setMode }) {
  const base = `${backend}/_elsa/identity/external-connections/users`;
  const links = async user => (await (await admin.request.get(`${base}/${encodeURIComponent(user)}/links`)).json()).items;
  const sessions = async () => (await (await admin.request.get(`${base}/${encodeURIComponent(target.id)}/sessions`)).json()).items;
  const originalLinks = await links(target.id);
  const conflict = await admin.newPage();
  try {
    await conflict.goto(componentUrl("access")); await conflict.getByLabel("Exact username or email").fill("pending-fixture-user");
    await conflict.getByRole("button", { name: "Find users", exact: true }).click();
    await conflict.locator(".external-auth-row").filter({ hasText: "pending-fixture-user" }).click();
    await expect(conflict.getByText("No identities are linked to this user.", { exact: true })).toBeVisible();
    await expect(conflict.getByText("No retained sessions for this user.", { exact: true })).toBeVisible();
    await capture(conflict, "pending-user-empty-access", { action: "inspect-unlinked-pending-user" });
    await conflict.getByLabel("Connection", { exact: true }).selectOption("deployment:partner");
    await conflict.getByLabel("External subject", { exact: true }).fill(subject);
    const response = conflict.waitForResponse(result => result.request().method() === "POST" && new URL(result.url()).pathname.endsWith("/links/attach"));
    await conflict.getByRole("button", { name: "Attach identity", exact: true }).click(); await confirm(conflict, "Attach identity");
    expect((await response).status()).toBe(409); await expect(conflict.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("already belongs");
    expect((await links("pending-fixture-user")).length).toBe(0); expect(await links(target.id)).toEqual(originalLinks);
    await capture(conflict, "identity-ownership-conflict", { action: "reject-already-owned-identity" });
    await conflict.waitForLoadState("networkidle");
  } finally { await conflict.close(); }
  for (const mode of ["before-write", "after-commit"]) {
    const beforeIds = new Set((await sessions()).map(session => session.id));
    const owner = await browser.newContext();
    try {
      const signIn = await owner.newPage(); await signIn.goto(backend + "/_elsa/identity/login?returnUrl=/backend/resource");
      await signIn.getByRole("button", { name: /^Continue with Partner(?: Preferred)?$/ }).click(); await expect(signIn).toHaveURL(backend + "/resource");
      const actual = await (await owner.request.get(backend + "/_elsa/identity/session")).json();
      expect(actual.subject).toBe(target.id); expect(actual.roles).toEqual(partnerSession.roles);
      const created = (await sessions()).filter(session => !beforeIds.has(session.id)); expect(created).toHaveLength(1);
      const selected = created[0]; await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
      const card = page.getByRole("region", { name: "Sessions", exact: true }).locator("article").filter({ has: page.getByLabel(`Session ID: ${selected.id}`, { exact: true }) });
      await page.setViewportSize({ width: 360, height: 844 }); await setMode("dark");
      await control(mode, target.id, selected.id);
      const response = page.waitForResponse(result => result.request().method() === "POST" && new URL(result.url()).pathname.endsWith(`/sessions/${selected.id}/revoke`));
      await card.getByRole("button", { name: "Revoke session", exact: true }).click(); await confirm(page, "Revoke session");
      expect((await response).status()).toBe(503); await expect(page.locator(".external-auth-shell:visible").getByRole("alert")).toContainText("Revocation could not be confirmed");
      const stored = (await sessions()).find(session => session.id === selected.id);
      expect(stored.state).toBe(mode === "before-write" ? "active" : "revoked");
      await capture(page, `revocation-${mode}-failure`, { action: `real-${mode}-503` });
      await page.getByRole("button", { name: "Refresh user access", exact: true }).click();
      if (mode === "before-write") {
        await expect(card.getByRole("button", { name: "Revoke session", exact: true })).toBeEnabled();
        await card.getByRole("button", { name: "Revoke session", exact: true }).click(); await confirm(page, "Revoke session");
        await expect(page.locator(".external-auth-notice")).toContainText("Session revoked");
      } else {
        await expect(card.getByRole("button", { name: "Revoke session", exact: true })).toBeDisabled();
        const csrf = await (await admin.request.get(backend + "/identity/external/antiforgery")).json();
        expect(csrf.headerName).toBeTruthy();
        const retry = await admin.request.post(`${base}/${encodeURIComponent(target.id)}/sessions/${selected.id}/revoke`,
          { headers: { [csrf.headerName]: csrf.requestToken }, data: { expectedRevision: stored.revision } });
        expect(retry.status()).toBe(200); expect((await retry.json()).alreadyRevoked).toBe(true);
      }
      expect((await sessions()).find(session => session.id === selected.id).state).toBe("revoked");
      expect((await (await owner.request.get(backend + "/_elsa/identity/session")).json()).status).toBe("anonymous");
      await capture(page, `revocation-${mode}-recovered`, { action: "refresh-prove-actual-revocation" });
    } finally { await owner.close(); }
  }
  await setMode("light"); await page.setViewportSize({ width: 1440, height: 1100 });
}

export async function exerciseSelfRevocation({ page, admin, backend, componentUrl, adminSession, expect, capture, confirm }) {
  await page.goto(componentUrl("access")); await page.getByLabel("Exact username or email").fill("admin");
  await page.getByRole("button", { name: "Find users", exact: true }).click();
  await page.locator(".external-auth-row").filter({ hasText: "admin" }).click();
  const card = page.getByRole("region", { name: "Sessions", exact: true }).locator("article").filter({ has: page.getByRole("heading", { name: /Current session/ }) });
  await expect(card).toHaveCount(1); await card.getByRole("button", { name: "Revoke session", exact: true }).click(); await confirm(page, "Revoke session");
  await expect(page.locator(".external-auth-notice")).toContainText("Current session revoked");
  await capture(page, "self-session-revoked", { action: "revoke-current-session" });
  expect((await (await admin.request.get(backend + "/_elsa/identity/session")).json()).status).toBe("anonymous");
  // Recording remains off during credentials, challenge and callback. No storageState is exported.
  await page.goto(backend + "/_elsa/identity/login?returnUrl=/backend/resource");
  await page.getByLabel("Username", { exact: true }).fill("admin"); await page.getByLabel("Password", { exact: true }).fill("Password123!");
  await Promise.all([page.waitForURL(backend + "/resource"), page.getByRole("button", { name: "Sign in", exact: true }).click()]);
  const renewed = await (await admin.request.get(backend + "/_elsa/identity/session")).json();
  expect(renewed.subject).toBe(adminSession.subject); expect(Array.isArray(renewed.roles)).toBe(true); expect(renewed.roles).toEqual(adminSession.roles);
  await page.goto(componentUrl("access")); await expect(page.getByRole("heading", { name: "User access", level: 2, exact: true })).toBeVisible();
  await page.getByLabel("Exact username or email").focus(); await capture(page, "self-session-reauthenticated", { action: "return-after-reauthentication" });
}
