# syntax=docker/dockerfile:1
# check=skip=InvalidDefaultArgInFrom

# The build must explicitly name the Foundation image used by prepare.py.
ARG FOUNDATION_IMAGE
FROM ${FOUNDATION_IMAGE}
ARG FOUNDATION_IMAGE
LABEL org.opencontainers.image.base.name="${FOUNDATION_IMAGE}"

# Match the host whose dependency graph and settings were used during preparation.
RUN --mount=type=bind,source=foundation-base.sha256,target=/tmp/foundation-base.sha256 \
    sha256sum -c /tmp/foundation-base.sha256 && mkdir -p /app/data

ENV ConnectionStrings__Elsa="Data Source=/app/data/elsa.db"

COPY profile-packages/ /app/profile-packages/
COPY profile-activities/ /app/profile-activities/
COPY appsettings.json shells.json nuplane.lock.json /app/
