# syntax=docker/dockerfile:1.26
# =============================================================================
# Elsa.Foundation.Host container image (minimal Nuplane + CShells host).
#
# Build context MUST be the repository root, because restore relies on repo-root
# configuration (Directory.Build.props, Directory.Packages.props, NuGet.config).
# This host has NO project references — every feature arrives at runtime as a
# .nupkg through the Nuplane directory feed at /app/packages — so, unlike the
# Workbench image, only the host project itself is copied. Example:
#
#   docker build -f src/Apps/Elsa.Foundation.Host/Dockerfile \
#                -t elsa-foundation-host:local-<sha> .
#
# Runtime configuration is supplied via environment variables (standard .NET
# double-underscore layering, e.g. Elsa__ModuleManagement__ApiKey) and an
# optional mounted shells.json at /app/shells.json.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Repo-root build configuration first, so the restore layer caches well.
# Directory.Packages.props carries central (and transitively pinned) versions;
# NuGet.config carries the feed list + packageSourceMapping, so restore is fully
# self-contained and needs no user-level NuGet config.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./

# The host project. No project references to bring along.
COPY src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj src/Apps/Elsa.Foundation.Host/
# NU1603: tolerate a restore-only warning when a transitive preview dependency
# resolves to a newer available version; does not relax real code warnings.
RUN dotnet restore src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
    -p:WarningsNotAsErrors=NU1603

# Now the host sources and publish (framework-dependent, portable).
COPY src/Apps/Elsa.Foundation.Host/ src/Apps/Elsa.Foundation.Host/
RUN dotnet publish src/Apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj \
    -c Release \
    --no-restore \
    -p:WarningsNotAsErrors=NU1603 \
    -o /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl gives the container a self-contained HTTP healthcheck (aspnet base ships none).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel listens on 8080 (aspnet base default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# Runtime write locations under the content root:
#   /app/packages   Nuplane directory feed (watched; mount a volume here to drop .nupkg files)
#   /app/.nuplane   Nuplane reconciliation state
# The non-root runtime user needs write access to them.
RUN mkdir -p /app/packages /app/.nuplane && chown -R $APP_UID:$APP_UID /app

# Liveness probe (process up + Kestrel serving). Deliberately /health/live, NOT
# /health/ready: readiness reports 503 while a shell is (re)activating, which is a
# normal transient state during a feed reconcile — it must not mark the container
# unhealthy.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=10 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Foundation.Host.dll"]
