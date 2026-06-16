import React, { useEffect, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import "./styles.css";

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

  useEffect(() => {
    moduleApi.host.http
      .getJson<Forecast[]>("/_elsa/studio/samples/weather-forecast")
      .then(setForecasts)
      .catch(e => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  return (
    <section>
      <div className="section-header">
        <div>
          <h2>Weather forecast</h2>
          <p>This module contributes both server behavior and React UI.</p>
        </div>
      </div>
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
