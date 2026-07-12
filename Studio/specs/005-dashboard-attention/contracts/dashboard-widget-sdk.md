# Dashboard Widget SDK Contract

## Contribution

```ts
type StudioDashboardWidgetSize = "small" | "medium" | "wide" | "full";

interface StudioDashboardWidgetLoadContext<TSettings> {
  settings: TSettings;
  signal: AbortSignal;
}

interface StudioDashboardWidgetSettings<TSettings> {
  schemaVersion: number;
  defaults: TSettings;
  descriptors: StudioSettingDescriptor[];
  validate(value: unknown): TSettings | null;
  migrate?(value: unknown, fromVersion: number): TSettings | null;
}

interface StudioDashboardWidgetBodyProps<TSnapshot, TSettings> {
  snapshot: TSnapshot | undefined;
  settings: TSettings;
}

interface StudioDashboardWidgetContribution<TSnapshot = unknown, TSettings = unknown> {
  id: string;
  moduleId: string;
  title: string;
  description?: string;
  order?: number;
  defaultVisible: boolean;
  defaultSize: StudioDashboardWidgetSize;
  supportedSizes: StudioDashboardWidgetSize[];
  permissions?: string[];
  minimumRefreshIntervalMs?: number;
  cacheLifetimeMs?: number;
  timeoutMs?: number;
  settings?: StudioDashboardWidgetSettings<TSettings>;
  load?(context: StudioDashboardWidgetLoadContext<TSettings>): Promise<TSnapshot>;
  component: React.ComponentType<StudioDashboardWidgetBodyProps<TSnapshot, TSettings>>;
}
```

The registry remains `api.dashboardWidgets`. This is a clean-break replacement;
legacy components that render their own card/frame are unsupported.

## Host Responsibilities

- Admission before loader invocation, including permissions and Host Policy.
- Title/frame, semantic size, responsive placement, keyboard layout actions.
- Loader cancellation, timeout, stale/cache timing, last/next refresh, retry.
- Loading, refreshing, empty, error, timed-out, and unavailable semantics.
- Settings dialog using shared Studio setting descriptors/editors.
- Preference validation/migration and scoped persistence.
- Cleanup when registry/session/tenant/host scope changes.

## Module Responsibilities

- Stable identity and truthful metadata.
- Cancellable loader that does not poll or establish hidden streams.
- Bounded snapshot and domain-specific body rendering.
- Settings defaults/validation and pure migrations when needed.
- Backend endpoints that independently enforce authorization and tenant scope.

## Default Host Configuration

- Global refresh: 5 minutes; choices Off/1/5/15/30 minutes.
- Widget timeout: 10 seconds, capped by Host Policy.
- Effective auto-refresh: slower of global choice and widget minimum.
- Manual refresh bypasses the auto-refresh interval but not cancellation/timeout.
- No automatic refresh while Dashboard is inactive.
