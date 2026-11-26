# Quickstart: Runtime Value Evidence Validation

## Prerequisites

- Install JavaScript dependencies with `pnpm install`.
- Have an Elsa foundation backend build that implements the contracts in `contracts/`.
- Run Studio against that backend with Host Policy allowing `DiagnosticSnapshot`.

## Studio Validation Commands

From the repository root:

```bash
pnpm --filter @elsa-workflows/studio-workflows test
pnpm lint
```

Expected outcome:

- Workflows tests pass.
- CSS lint passes without raw color literals in module CSS.
- No accessibility lint failures are introduced by settings controls or snapshot tree rendering.

## Backend Validation Commands

From `/Users/sipke/Projects/Elsa/elsa-foundation`:

```bash
dotnet test
```

Expected outcome:

- Runtime payload capture policy tests show input/output defaults produce Diagnostic Snapshots when Host Policy allows them.
- Snapshot generation tests cover bounds, redaction, unsupported values, and safe errors.
- Activity execution inspection API tests return Diagnostic Snapshot evidence without raw payloads in snapshot mode.

## Manual End-to-End Scenario

1. Start the backend with Host Policy maximum `DiagnosticSnapshot`.
2. Open Studio and navigate to Workflows runtime diagnostics settings.
3. Verify requested level is `DiagnosticSnapshot` and effective level is `DiagnosticSnapshot`.
4. Run a workflow containing a completed `WriteLine` activity with a runtime input value.
5. Open `Workflows > Runs`, select the new run, and select the completed activity in the Activity tab.
6. Verify the Activity metadata remains visible.
7. Verify the Inputs section shows the `Text` input as a Diagnostic Snapshot.
8. Verify the Outputs section renders an empty or not-captured state if the activity has no outputs.

## Host Policy Cap Scenario

1. Start the backend with Host Policy maximum `Metadata`.
2. Request `DiagnosticSnapshot` in Workflows runtime diagnostics settings.
3. Verify Studio displays requested `DiagnosticSnapshot`, effective `Metadata`, and a host-policy limitation reason.
4. Run a new workflow instance.
5. Verify the Activity inspector shows metadata-only/not-captured value evidence rather than raw values.

## Redaction And Bounds Scenario

1. Run a workflow activity with nested input containing a sensitive field name such as `password`.
2. Include a long string and an array longer than the configured limit.
3. Inspect the completed activity.
4. Verify sensitive values render as redaction markers.
5. Verify long strings and arrays render truncation markers with safe counts/previews.

## Future Reference Fixture Scenario

Use a Studio UI fixture response containing a `payloadReference` Diagnostic Snapshot leaf.

Expected outcome:

- The Activity inspector renders safe display metadata.
- Reveal/download actions are unavailable until reference resolution is implemented.
- Provider internals or protected values are not displayed.
