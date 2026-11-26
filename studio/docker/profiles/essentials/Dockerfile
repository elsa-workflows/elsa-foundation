# syntax=docker/dockerfile:1

ARG STUDIO_VERSION=specify-release
ARG FOUNDATION_IMAGE=elsaworkflows/elsa-studio-foundation:${STUDIO_VERSION}

FROM ${FOUNDATION_IMAGE}
ARG FOUNDATION_IMAGE
LABEL org.opencontainers.image.base.name="${FOUNDATION_IMAGE}"

COPY profile/ /studio-profile/
RUN version=$(cat /studio-profile/version) \
    && grep -Fq "\"Elsa.Studio.Core/$version\"" /app/Elsa.Studio.Foundation.Host.deps.json \
    || { echo "The Studio Foundation image must provide Elsa.Studio.Core $version." >&2; exit 1; }

COPY --chown=app:app appsettings.json appsettings.Development.json nuplane-management.json shells.json /app/
