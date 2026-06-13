import React, { useEffect, useState } from "react";
import type { ElsaAdminModuleApi } from "@elsa-workflows/admin-sdk";
import "./styles.css";

interface Forecast {
  date: string;
  temperatureC: number;
  temperatureF: number;
  summary: string;
}

let moduleApi: ElsaAdminModuleApi;

export function register(api: ElsaAdminModuleApi) {
  moduleApi = api;

  api.navigation.add({
    id: "weather-forecast-sample",
    label: "Weather",
    path: "/weather",
    order: 120
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

  useEffect(() => {
    moduleApi.host.http
      .getJson<Forecast[]>("/_elsa/samples/weather-forecast")
      .then(setForecasts)
      .catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  return (
    <section>
      <header className="page-header">
        <h1>Weather forecast</h1>
        <p>This module contributes both server behavior and React UI.</p>
      </header>
      {error ? <div className="error-state">{error}</div> : null}
      <div className="weather-table">
        <div className="weather-row weather-heading">
          <span>Date</span>
          <span>Summary</span>
          <span>Temperature</span>
        </div>
        {forecasts.map(forecast => (
          <div className="weather-row" key={forecast.date}>
            <span>{forecast.date}</span>
            <strong>{forecast.summary}</strong>
            <span>{forecast.temperatureC}C / {forecast.temperatureF}F</span>
          </div>
        ))}
      </div>
    </section>
  );
}
