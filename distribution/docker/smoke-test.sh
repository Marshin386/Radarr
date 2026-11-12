#!/bin/sh
# Start a locally loaded image and fail unless /ping returns OK, the Linux
# platform assembly is present, and /api/v3/system/status reports the fork version.
set -eu

image="${1:-radarr:smoke}"
expected="${2:-}"
name=radarr-smoke
log=/tmp/radarr-smoke.log

if [ -z "${expected}" ]; then
    echo 'expected fork version is required' >&2
    exit 1
fi

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

api_key="$(docker exec "${name}" sed -n 's:.*<ApiKey>\([^<]*\)</ApiKey>.*:\1:p' /config/config.xml)"
if [ -z "${api_key}" ]; then
    echo 'could not read the API key from /config/config.xml' >&2
    exit 1
fi

status="$(curl -fsS -H "X-Api-Key: ${api_key}" http://127.0.0.1:7878/api/v3/system/status)"
echo "system status: ${status}"

if ! printf '%s' "${status}" | grep -Eq "\"version\": *\"${expected}\""; then
    echo "system status version is not ${expected}" >&2
    exit 1
fi

if ! printf '%s' "${status}" | grep -Eq "\"packageVersion\": *\"${expected}\""; then
    echo "system status packageVersion is not ${expected}" >&2
    exit 1
fi

docker logs "${name}" > "${log}" 2>&1 || true
if grep -E 'Could not load file or assembly|FileNotFoundException|DllNotFoundException' "${log}"; then
    echo 'Radarr log contains a missing-assembly error' >&2
    exit 1
fi

if ! grep -q "Version ${expected}" "${log}"; then
    echo "startup log did not report Version ${expected}" >&2
    exit 1
fi

flat=/config/flat-root-smoke
docker exec "${name}" mkdir -p "${flat}"
docker exec "${name}" chown radarr:radarr "${flat}"

created="$(curl -fsS -H "X-Api-Key: ${api_key}" -H 'Content-Type: application/json' \
    -d "{\"path\":\"${flat}\",\"folderLayout\":\"movieFolder\"}" \
    http://127.0.0.1:7878/api/v3/rootfolder)"
echo "created root folder: ${created}"
root_id="$(printf '%s' "${created}" | sed -n 's/.*"id": *\([0-9][0-9]*\).*/\1/p')"
if [ -z "${root_id}" ]; then
    echo 'creating a root folder did not return an id' >&2
    exit 1
fi

updated="$(curl -fsS -X PUT -H "X-Api-Key: ${api_key}" -H 'Content-Type: application/json' \
    -d "{\"id\":${root_id},\"path\":\"${flat}\",\"folderLayout\":\"flat\"}" \
    "http://127.0.0.1:7878/api/v3/rootfolder/${root_id}")"
echo "updated root folder: ${updated}"

read_back="$(curl -fsS -H "X-Api-Key: ${api_key}" "http://127.0.0.1:7878/api/v3/rootfolder/${root_id}")"
echo "root folder read back: ${read_back}"
if ! printf '%s' "${read_back}" | grep -Eq '"folderLayout": *"flat"'; then
    echo 'root folder layout was not saved as flat' >&2
    exit 1
fi

echo "smoke test passed: /ping returned OK, version is ${expected}, and a root folder layout updated to flat"
