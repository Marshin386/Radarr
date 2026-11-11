# syntax=docker/dockerfile:1
# Multi-arch image for the Marshin386 Radarr fork (custom/flat folder layouts).
# linux/amd64 and linux/arm64. Listens on 7878, app data in /config.

FROM --platform=$BUILDPLATFORM node:24-bookworm AS ui
WORKDIR /src
COPY package.json yarn.lock .yarnrc tsconfig.json ./
COPY frontend ./frontend
RUN corepack enable \
    && corepack prepare yarn@1.22.22 --activate \
    && yarn install --frozen-lockfile --network-timeout 120000 \
    && yarn build --env production

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
COPY --from=ui /src/_output/UI /tmp/UI
RUN case "${TARGETARCH}" in \
        amd64) RID=linux-x64 ;; \
        arm64) RID=linux-arm64 ;; \
        *) echo "Unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && dotnet publish src/NzbDrone.Console/Radarr.Console.csproj \
        -c Release \
        -f net10.0 \
        -r "${RID}" \
        --self-contained true \
        -p:EnableAnalyzers=false \
        -o /out/bin \
    && cp -a /tmp/UI /out/bin/UI \
    && FORK_VERSION="$(tr -d '[:space:]' < distribution/fork-version)" \
    && printf 'ReleaseVersion=%s\n' "${FORK_VERSION}" > /out/bin/release_info \
    && printf 'PackageVersion=%s\nPackageAuthor=Marshin386\nUpdateMethod=Docker\nPackageGlobalMessage=Marshin386 Radarr fork %s (custom folder layouts). Updates are delivered by replacing this image.\n' \
        "${FORK_VERSION}" "${FORK_VERSION}" > /out/package_info \
    && chmod +x /out/bin/Radarr

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
ARG TARGETARCH
# gosu is not in Ubuntu main. Pin the upstream binary and check its checksum.
ARG GOSU_VERSION=1.20
RUN set -eux; \
    if [ -f /etc/apt/sources.list.d/ubuntu.sources ]; then \
        sed -i 's/^Components: .*/Components: main universe/' /etc/apt/sources.list.d/ubuntu.sources; \
    fi; \
    printf 'Acquire::Retries "5";\n' > /etc/apt/apt.conf.d/80-retries; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        ffmpeg \
        libsqlite3-0 \
        mediainfo \
        tzdata; \
    case "${TARGETARCH}" in \
        amd64) gosu_sha=b1892636c281590f34bc63c1118f058cd9de66c4ea463772e3909672d8b302ac ;; \
        arm64) gosu_sha=c216cb762520edb5ab0acb21cb9bc4bcc5f1544d365e8952a21514f04ea233ee ;; \
        *) echo "Unsupported TARGETARCH=${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL -o /usr/local/bin/gosu "https://github.com/tianon/gosu/releases/download/${GOSU_VERSION}/gosu-${TARGETARCH}"; \
    echo "${gosu_sha}  /usr/local/bin/gosu" | sha256sum -c -; \
    chmod 0755 /usr/local/bin/gosu; \
    gosu --version; \
    rm -rf /var/lib/apt/lists/*

COPY distribution/docker/entrypoint.sh /entrypoint.sh
COPY --from=build /out/bin /app/radarr/bin
COPY --from=build /out/package_info /app/radarr/package_info
RUN chmod +x /entrypoint.sh /app/radarr/bin/Radarr \
    && mkdir -p /config \
    && useradd -u 1000 -U -d /config -M -s /usr/sbin/nologin radarr \
    && chown -R radarr:radarr /config

ENV PUID=1000 \
    PGID=1000 \
    TZ=Etc/UTC \
    UMASK=022

EXPOSE 7878
VOLUME /config
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:7878/ping || exit 1
ENTRYPOINT ["/entrypoint.sh"]
