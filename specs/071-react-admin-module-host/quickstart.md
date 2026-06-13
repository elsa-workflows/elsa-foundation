# Quickstart: React Admin Module Host

1. Build frontend assets:

   ```bash
   npm ci --prefix src/Elsa/Admin/Web/Client
   npm run build --prefix src/Elsa/Admin/Web/Client
   npm ci --prefix src/Elsa/Admin/Samples/Dashboard/Client
   npm run build --prefix src/Elsa/Admin/Samples/Dashboard/Client
   npm ci --prefix src/Elsa/Admin/Samples/WeatherForecast/Client
   npm run build --prefix src/Elsa/Admin/Samples/WeatherForecast/Client
   ```

2. Run tests:

   ```bash
   dotnet test tests/Elsa/Admin/Tests/Elsa.Admin.Tests.csproj
   npm test --prefix src/Elsa/Admin/Web/Client
   npm test --prefix src/Elsa/Admin/Samples/Dashboard/Client
   npm test --prefix src/Elsa/Admin/Samples/WeatherForecast/Client
   ```

3. Start `Elsa.Server` and verify:

   - `/` opens the existing demo app.
   - `/demo` opens the existing demo app.
   - `/admin` opens the modular admin shell.
   - `/_elsa/admin/modules` returns dashboard and weather manifests.
   - `/admin/diagnostics/modules` shows module load diagnostics.
   - `/admin/weather` renders deterministic weather data.
