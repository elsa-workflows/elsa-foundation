# syntax=docker/dockerfile:1

# =============================================================================
# Elsa.Server production image
#
# Build context MUST be the repository root, because the build relies on
# repo-root configuration (Directory.Packages.props, NuGet.config) and the
# project references reach across the whole src/ tree. Example:
#
#   docker build -f src/Apps/Elsa.Server/Dockerfile -t elsa-server:local .
#
# Runtime configuration is supplied via environment variables (standard .NET
# double-underscore layering, e.g. Elsa__ModuleManagement__ApiKey) and an
# optional mounted shells.json. See docs/docker.md for the full surface.
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy repo-root build configuration first so restore layers cache well.
# Note: this repo has no root Directory.Build.props/targets.
#
# NuGet.config is the repo-root config. Its packageSourceMapping maps every
# package the solution uses to an explicit source, so restore is fully
# self-contained here and needs no user-level NuGet config fallback.
COPY Directory.Packages.props ./
COPY NuGet.config ./
COPY Elsa.Server.slnx ./

# Copy the source tree (tests, docs, local dev artifacts are excluded via .dockerignore).
COPY src/ ./src/

# Restore and publish only the server host; project references are pulled in transitively.
#
# WarningsNotAsErrors=NU1603 tolerates a restore-only warning that can appear in a clean
# environment when a transitive preview dependency resolves to a newer available version,
# without relaxing the repo's TreatWarningsAsErrors for real code warnings.
RUN dotnet restore src/Apps/Elsa.Server/Elsa.Server.csproj \
        -p:WarningsNotAsErrors=NU1603
RUN dotnet publish src/Apps/Elsa.Server/Elsa.Server.csproj \
        -c Release \
        --no-restore \
        -p:WarningsNotAsErrors=NU1603 \
        -o /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is added solely to give the container a self-contained HTTP healthcheck
# (the aspnet base image ships no HTTP client). Installed before dropping to the
# non-root user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Kestrel listens on 8080 (the aspnet base image default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

# The server writes state under the content root at runtime:
#   /app/.nuplane            Nuplane reconciliation state
#   /app/packages            Nuplane directory feed (watched; optionally a volume mount)
#   /app/.elsa               GitHubCopilotAgent working directory
#   /app/extension-builder   ExtensionBuilder workspace
#   *.db / *.sqlite*.db      SQLite fallback stores (only when a SQLite lane is composed)
# so the non-root runtime user needs write access to the app directory.
RUN mkdir -p /app/packages /app/.nuplane /app/.elsa && chown -R $APP_UID:$APP_UID /app

# The root endpoint returns a small Healthy JSON payload (see Program.cs).
HEALTHCHECK --interval=15s --timeout=3s --start-period=40s --retries=10 \
    CMD curl -fsS http://localhost:8080/ || exit 1

# Run as the non-root user provided by the base image.
USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Server.dll"]
