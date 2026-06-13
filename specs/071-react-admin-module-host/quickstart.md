# Quickstart: React Admin Module Host

1. Build frontend assets:

   ```bash
   npm ci --prefix src/elsa/Admin/Web/Client
   npm run build --prefix src/elsa/Admin/Web/Client
   npm ci --prefix src/elsa/Admin/Samples/Dashboard/Client
   npm run build --prefix src/elsa/Admin/Samples/Dashboard/Client
   npm ci --prefix src/elsa/Admin/Samples/WeatherForecast/Client
   npm run build --prefix src/elsa/Admin/Samples/WeatherForecast/Client
   ```

2. Run tests:

   ```bash
   dotnet test tests/Elsa/Admin/Tests/Elsa.Admin.Tests.csproj
   npm test --prefix src/elsa/Admin/Web/Client
   npm test --prefix src/elsa/Admin/Samples/Dashboard/Client
   npm test --prefix src/elsa/Admin/Samples/WeatherForecast/Client
   ```

3. Start `Elsa.Studio.Web` and verify:

   ```bash
   dotnet run --project src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
   ```

   - `/` opens the modular admin shell.
   - `/_elsa/admin/modules` returns dashboard and weather manifests.
   - `/diagnostics/modules` shows module load diagnostics.
   - `/weather` renders deterministic weather data.
