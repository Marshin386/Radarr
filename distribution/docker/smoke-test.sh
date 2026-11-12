#!/bin/sh
# Start a locally loaded image and fail unless /ping returns OK and the
# Linux platform assembly is present. Used by the Docker workflow.
set -eu

image="${1:-radarr:smoke}"
name=radarr-smoke
log=/tmp/radarr-smoke.log

docker rm -f "${name}" >/dev/null 2>&1 || true
docker run -d --name "${name}" -p 7878:7878 -e PUID=1000 -e PGID=1000 "${image}"

cleanup() {
    echo '--- container logs ---'
    docker logs "${name}" > "${log}" 2>&1 || true
    cat "${log}" || true
    docker rm -f "${name}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker exec "${name}" test -s /app/radarr/bin/Radarr.Mono.dll
docker exec "${name}" sh -c 'ls /app/radarr/bin/Openur.Mono.Unix.* /app/radarr/bin/libMono.Unix.* >/dev/null'

i=0
while [ "${i}" -lt 40 ]; do
    if curl -fsS http://127.0.0.1:7878/ping | grep -q '"status":"OK"'; then
        break
    fi
    i=$((i + 1))
    sleep 3
done

if ! curl -fsS http://127.0.0.1:7878/ping | grep -q '"status":"OK"'; then
    echo 'Radarr did not answer /ping with status OK' >&2
    exit 1
fi

docker logs "${name}" > "${log}" 2>&1 || true
if grep -E 'Could not load file or assembly|FileNotFoundException|DllNotFoundException' "${log}"; then
    echo 'Radarr log contains a missing-assembly error' >&2
    exit 1
fi

echo 'smoke test passed: /ping returned OK and Radarr.Mono.dll is present'
