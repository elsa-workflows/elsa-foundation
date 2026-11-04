# Validate the prototype

1. Install `pnpm --dir Studio install --frozen-lockfile` and Playwright Chromium.
2. Run Secrets Vitest suite, typecheck/build, and the browser suites `secret-field-prototype.spec.ts` and `secret-scopes.spec.ts` from Studio.
3. Start the browser-fixture Vite config on an owned free port and open `/secret-field-prototype.html`. Select theme/appearance and explore the actual Create secret dialog and representative state fields. The preview is isolated; saves use demonstration metadata.
4. Commit changes, then run `python3 tools/ux-acceptance/run.py --output <new-owned-output>` for real composed-host acceptance. Inspect feature-specific screenshots, trace and report at that source SHA. Existing stores/listeners must not be used.
5. Review theme captures, empty/populated/invalid/submitting/read-only and narrow states. Record dimensions and shared-component recommendations in [findings.md](findings.md); keep acceptance incomplete until independent composed inspection passes.
