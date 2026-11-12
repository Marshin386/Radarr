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
    -d "{\"path\":\"${flat}\"}" \
    http://127.0.0.1:7878/api/v3/rootfolder)"
echo "created root folder: ${created}"

RADARR_API_KEY="${api_key}" python3 - <<'PY'
import json, os, urllib.error, urllib.request
base = "http://127.0.0.1:7878"
key = os.environ["RADARR_API_KEY"]

def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(
        base + path,
        data=data,
        method=method,
        headers={
            "X-Api-Key": key,
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")
        raise SystemExit("%s %s failed: %s %s" % (method, path, error.code, detail))

naming = call("GET", "/api/v3/config/naming")
naming["folderLayout"] = "flat"
call("PUT", "/api/v3/config/naming/%s" % naming["id"], naming)
saved = call("GET", "/api/v3/config/naming")
print("naming after save:", saved.get("folderLayout"))
if saved.get("folderLayout") != "flat":
    raise SystemExit("naming folderLayout did not persist")
roots = call("GET", "/api/v3/rootfolder")
print("roots after naming save:", roots)
if not roots or roots[0].get("folderLayout") != "flat":
    raise SystemExit("library structure was not copied onto the root folder")
naming["folderLayout"] = "movieFolder"
call("PUT", "/api/v3/config/naming/%s" % naming["id"], naming)
host = call("GET", "/api/v3/config/host")
host["authenticationMethod"] = "forms"
host["username"] = "radarr"
host["password"] = "radarr-test"
host["passwordConfirmation"] = "radarr-test"
call("PUT", "/api/v3/config/host/%s" % host["id"], host)
print("forms auth configured for the UI check")
PY

if [ ! -d "${NODE_PATH:-}/playwright" ]; then
    echo 'Playwright is not installed; the Library Structure UI check cannot run' >&2
    exit 1
fi

node distribution/docker/library-structure-ui.cjs

echo "smoke test passed: /ping returned OK, version is ${expected}, and Library Structure saved in the UI"
