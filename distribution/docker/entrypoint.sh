#!/bin/sh
# Drop privileges to PUID:PGID and start Radarr with /config as app data.
# Equivalent to the linuxserver.io Radarr image environment variables.
set -eu

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"
TZ="${TZ:-Etc/UTC}"
UMASK="${UMASK:-022}"

if [ -f "/usr/share/zoneinfo/${TZ}" ]; then
    ln -snf "/usr/share/zoneinfo/${TZ}" /etc/localtime
    printf '%s\n' "${TZ}" > /etc/timezone
fi

umask "${UMASK}"

if getent group radarr >/dev/null 2>&1; then
    groupmod -o -g "${PGID}" radarr
else
    groupadd -o -g "${PGID}" radarr
fi

if id radarr >/dev/null 2>&1; then
    usermod -o -u "${PUID}" -g radarr -d /config radarr
else
    useradd -o -u "${PUID}" -g radarr -d /config -M -s /usr/sbin/nologin radarr
fi

mkdir -p /config
chown radarr:radarr /config

cd /app/radarr/bin
exec gosu radarr:radarr /app/radarr/bin/Radarr -nobrowser -data=/config
