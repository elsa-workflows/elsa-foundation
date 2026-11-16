import React, { useCallback, useEffect, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioPage, StudioPageHeader, StudioButton, StudioTableContainer, StudioCollectionFooter, StudioResultState, SkeletonRows } from "@elsa-workflows/studio-ui";

interface Forecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}

let moduleApi: ElsaStudioModuleApi;

export function register(api: ElsaStudioModuleApi) {
  moduleApi = api;

  api.navigation.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/weather",
    order: 120,
    iconColor: "#14b8a6"
  });

  api.routes.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/weather",
    component: WeatherPage
  });
}

export function WeatherPage() {
  const [forecasts, setForecasts] = useState<Forecast[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setForecasts(await moduleApi.host.http.getJson<Forecast[]>("/_elsa/studio/samples/weather-forecast"));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setLoading(false);
    }
  }, []);
  useEffect(() => { void load(); }, [load]);

  return (
    <StudioPage aria-labelledby="weather-title">
      <StudioPageHeader title="Weather forecast" titleId="weather-title" headingLevel={2}
        description="This module contributes both server behavior and React UI."
        actions={<StudioButton onClick={() => void load()} disabled={loading}>Refresh</StudioButton>} />
      {loading ? <SkeletonRows label="Loading weather forecast" /> : error ? (
        <StudioResultState headingLevel={3} tone="danger" title="Weather forecast could not be loaded" description={error}
          actions={<StudioButton onClick={() => void load()}>Retry</StudioButton>} />
      ) : forecasts.length === 0 ? <StudioResultState headingLevel={3} title="No forecast available" description="Refresh to check for new forecasts." /> : (
        <StudioTableContainer role="region" aria-label="Weather forecast table" tabIndex={0}>
          <table className="studio-table" aria-label="Weather forecast">
            <thead><tr><th scope="col">Date</th><th scope="col">Summary</th><th scope="col">Temperature</th></tr></thead>
            <tbody>{forecasts.map(forecast => <tr key={forecast.date}>
              <td>{forecast.date}</td><td>{forecast.summary}</td><td>{forecast.temperatureC}°C / {forecast.temperatureF}°F</td>
            </tr>)}</tbody>
          </table>
        </StudioTableContainer>
      )}
      {!loading && !error ? <StudioCollectionFooter>{forecasts.length} forecasts</StudioCollectionFooter> : null}
    </StudioPage>
  );
}
