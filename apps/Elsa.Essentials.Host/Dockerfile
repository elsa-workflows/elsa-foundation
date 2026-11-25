# syntax=docker/dockerfile:1.26
# Keep this pin in step with the other host Dockerfiles: it must stay within a few releases of the BuildKit
# docker/github-builder runs (see the note in apps/Elsa.Workbench.Host/Dockerfile and the @v1.16.0 pin in
# .github/workflows/docker.yml).

# =============================================================================
# Elsa.Essentials.Host container image (Nuplane + CShells host with the curated workflow essentials compiled in).
#
# Build context MUST be the repository root: restore relies on repo-root configuration (Directory.Build.props,
# Directory.Packages.props, NuGet.config), and the curated features are ProjectReferences across essentials/src.
# Add-on features still arrive at runtime as .nupkg files through the Nuplane directory feed at /app/packages.
# Example:
#
#   docker build -f apps/Elsa.Essentials.Host/Dockerfile -t elsa-essentials:local .
#
# Runtime configuration is supplied via environment variables (standard .NET double-underscore layering, e.g.
# ConnectionStrings__Elsa or Elsa__ModuleManagement__ApiKey) and an optional mounted shells.json at /app/shells.json.
# With no ASPNETCORE_ENVIRONMENT the container runs as Production, which blanks the demo keys and the seeded admin
# password and validates migrations instead of applying them (see apps/Elsa.Essentials.Host/README.md).
# =============================================================================

# ---- Build stage ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Repo-root build configuration first, so the restore layer caches well. *.props and *.targets rather than names,
# because Directory.Build.props imports sibling root files and restore ignores a missing import -- see the same step in
# the Workbench Dockerfile. NuGet.config carries the feed list and packageSourceMapping, so restore needs no user-level
# NuGet config. .editorconfig carries the naming analyzer's per-domain retired terms, so the image compiles under the
# same naming rules as CI.
COPY *.props *.targets .editorconfig NuGet.config ./

# Restore inputs only, at their source-tree paths, so ordinary source changes keep the restore layer. The curated set
# is a large project graph across essentials/src, so the recursive patterns cover it, with each project's committed
# NuGet lock file (#2122), instead of a hand-kept list that would drift whenever the set changes. essentials/tests is
# excluded by .dockerignore. The host comes with the computed-production-settings swap its csproj imports from apps/
# (#2126): MSBuild treats a missing <Import> as an error, so it has to be here before restore.
COPY --parents essentials/**/*.csproj essentials/**/*.targets essentials/**/packages.lock.json apps/ComputedProductionSettings.targets apps/Elsa.Essentials.Host/Elsa.Essentials.Host.csproj apps/Elsa.Essentials.Host/packages.lock.json ./

# Locked (#2122): restores exactly the versions the committed lock files name, and fails when one no longer matches
# its project or names a version the feed no longer has. See docs/reference/nuget-lock-files.md.
# NU1603: tolerate a restore-only warning when a transitive preview dependency resolves to a newer available version;
# does not relax real code warnings.
RUN dotnet restore apps/Elsa.Essentials.Host/Elsa.Essentials.Host.csproj \
    --locked-mode \
    -p:WarningsNotAsErrors=NU1603

# Now the sources: the curated features and the analyzers under essentials/, and the host itself.
COPY essentials/ ./essentials/
COPY apps/Elsa.Essentials.Host/ apps/Elsa.Essentials.Host/

# Package versions (ADR 0067, spec 150, #2084, #2126), handled exactly as in the Foundation host Dockerfile. The Docker
# Images workflow passes ELSA_PACKAGE_VERSIONS: the pack-properties file the version calculator writes for this commit,
# gzipped and base64-encoded onto one line. Both commands below read it through -p:CustomBeforeDirectoryBuildProps,
# which gives every Elsa assembly, and every Elsa package in the deps.json, the version the feed carries, and makes
# Elsa.Essentials.Host.csproj ship ComputedVersions/appsettings.Production.json, which declares the Elsa shares
# host-provided. The restore has to see it too: deps.json takes a project reference's version from the restore, not
# the build. The restore above stays free of it and keeps its cache; this one only rewrites the assets files.
#
# Left at its default, source-build, the image is built as a source build: dev versions and the committed
# appsettings.Production.json, which leaves the Elsa shares undeclared. That is what `docker build` does directly. An
# empty value is refused: it is what the workflow passes when it lost the versions.
ARG ELSA_PACKAGE_VERSIONS=source-build

RUN set -- \
    && case "$ELSA_PACKAGE_VERSIONS" in \
        source-build) ;; \
        "") echo "ELSA_PACKAGE_VERSIONS is empty. Pass the computed versions, or leave it unset for a source build." >&2; exit 1 ;; \
        *) mkdir -p /tmp/elsa \
            && printf '%s' "$ELSA_PACKAGE_VERSIONS" | base64 -d | gunzip > /tmp/elsa/package-versions.props \
            && set -- -p:CustomBeforeDirectoryBuildProps=/tmp/elsa/package-versions.props ;; \
    esac \
    && dotnet restore apps/Elsa.Essentials.Host/Elsa.Essentials.Host.csproj \
        --locked-mode \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
    && dotnet publish apps/Elsa.Essentials.Host/Elsa.Essentials.Host.csproj \
        -c Release \
        --no-restore \
        -p:WarningsNotAsErrors=NU1603 \
        "$@" \
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
#   /app/packages   Nuplane directory feed for add-on packages (watched; mount a volume here to drop .nupkg files)
#   /app/.nuplane   Nuplane reconciliation state
#   /app/data       SQLite files meant to outlive the container (mount a named volume here and point
#                   ConnectionStrings__Elsa at it, e.g. "Data Source=/app/data/elsa-essentials.db")
#                   In Production also point the OpenIddict token store there:
#                   CShells__Shells__default__Features__FoundationIdentityOpenIddict__ConnectionString
#   *.db            the default SQLite stores in the content root: elsa-essentials.db and, in Production, elsa-identity.db
# The non-root runtime user needs write access to them. The Data Protection key ring is not among them: unless
# Elsa:DataProtection:EntityFrameworkCore:Enabled keeps it in the platform database, ASP.NET Core keeps it in the
# runtime user's home directory, which no volume covers, so a recreated container signs everyone out
# (docs/docker.md, "Data Protection keys").
RUN mkdir -p /app/packages /app/.nuplane /app/data && chown -R $APP_UID:$APP_UID /app

# Liveness probe (process up + Kestrel serving). Deliberately /health/live, not /health/ready: readiness reports 503
# while a shell is (re)activating, which is a normal transient state during a feed reconcile, so it must not mark the
# container unhealthy.
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=10 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

USER $APP_UID

ENTRYPOINT ["dotnet", "Elsa.Essentials.Host.dll"]
