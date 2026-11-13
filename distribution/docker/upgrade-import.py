#!/usr/bin/env python3
"""Import an upgrade in place and into a designated folder for flat and movie-folder layouts."""
import json
import os
import subprocess
import time
import urllib.error
import urllib.request

BASE = os.environ.get("RADARR_URL", "http://127.0.0.1:7878")
KEY = os.environ["RADARR_API_KEY"]
CONTAINER = os.environ.get("RADARR_CONTAINER", "radarr-smoke")
DEST = "/config/upgrade-dest"


def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(
        BASE + path,
        data=data,
        method=method,
        headers={
            "X-Api-Key": KEY,
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            raw = response.read().decode()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")
        raise SystemExit("%s %s failed: %s %s" % (method, path, error.code, detail))


def docker(script):
    subprocess.check_call(["docker", "exec", CONTAINER, "sh", "-c", script])


def exists(path):
    quoted = "'" + path.replace("'", "'\"'\"'") + "'"
    return subprocess.run(
        ["docker", "exec", CONTAINER, "sh", "-c", "test -e %s" % quoted],
        check=False,
    ).returncode == 0


def quote(path):
    return "'" + path.replace("'", "'\"'\"'") + "'"


def wait_for_commands():
    for _ in range(90):
        commands = call("GET", "/api/v3/command") or []
        busy = [command for command in commands if command.get("status") not in ("completed", "failed", "aborted")]
        if not busy:
            failed = [command for command in commands if command.get("status") == "failed" and command.get("name") == "ManualImport"]
            if failed:
                raise SystemExit("manual import failed: %s" % failed[-1].get("message"))
            return
        time.sleep(1)
    raise SystemExit("import command did not finish")


def set_layout(layout):
    naming = call("GET", "/api/v3/config/naming")
    naming["folderLayout"] = layout
    naming["movieFolderFormat"] = "{Movie Title} ({Release Year})"
    call("PUT", "/api/v3/config/naming/%s" % naming["id"], naming)


def set_profile(profile, placement, destination=None):
    profile["upgradeAllowed"] = True
    profile["upgradeFilePlacement"] = placement
    profile["upgradeDestination"] = destination
    call("PUT", "/api/v3/qualityprofile/%s" % profile["id"], profile)


def import_file(movie_id, path, quality_id):
    call(
        "POST",
        "/api/v3/command",
        {
            "name": "ManualImport",
            "importMode": "move",
            "files": [
                {
                    "path": path,
                    "movieId": movie_id,
                    "quality": {"quality": {"id": quality_id}, "revision": {"version": 1, "real": 0}},
                    "languages": [{"id": 1}],
                }
            ],
        },
    )
    wait_for_commands()
    movie = call("GET", "/api/v3/movie/%s" % movie_id)
    imported = ((movie.get("movieFile") or {}).get("quality") or {}).get("quality") or {}
    if imported.get("id") != quality_id:
        raise SystemExit("import of %s did not become the movie file: %s" % (path, movie.get("movieFile")))
    return movie


def write_file(path):
    docker("mkdir -p %s && printf x > %s && chown -R radarr:radarr %s" % (quote(os.path.dirname(path)), quote(path), quote(os.path.dirname(path))))


def main():
    set_layout("movieFolder")
    movies = call("GET", "/api/v3/movie")
    movie = next(item for item in movies if item.get("tmdbId") == 603)
    profile = call("GET", "/api/v3/qualityprofile/%s" % movie["qualityProfileId"])
    set_profile(profile, "replaceInCurrentLocation", None)
    profile = call("GET", "/api/v3/qualityprofile/%s" % profile["id"])
    movie_id = movie["id"]
    original = movie["path"].rstrip("/")
    folder_name = os.path.basename(original)

    current = "%s/The.Matrix.1999.HDTV.mkv" % original
    sidecar = "%s/The.Matrix.1999.HDTV.en.srt" % original
    write_file(current)
    write_file(sidecar)
    import_file(movie_id, current, 4)

    movie = call("GET", "/api/v3/movie/%s" % movie_id)
    if movie["path"].rstrip("/") != original:
        raise SystemExit("seeding the file changed the movie path to %s" % movie["path"])

    upgrade = "/config/downloads/The.Matrix.1999.BluRay.mkv"
    write_file(upgrade)
    import_file(movie_id, upgrade, 7)
    movie = call("GET", "/api/v3/movie/%s" % movie_id)
    if movie["path"].rstrip("/") != original:
        raise SystemExit("replace-in-place changed the path to %s" % movie["path"])
    if not exists("%s/The.Matrix.1999.BluRay.mkv" % original):
        raise SystemExit("upgrade was not imported into the current folder")
    if exists(sidecar):
        raise SystemExit("old sidecar was not removed with the replaced file")
    print("replace in current location kept", movie["path"])

    def seed_again():
        movie_now = call("GET", "/api/v3/movie/%s" % movie_id)
        if movie_now["path"].rstrip("/") != original:
            movie_now["path"] = original
            call("PUT", "/api/v3/movie/%s?moveFiles=false" % movie_id, movie_now)
            movie_now = call("GET", "/api/v3/movie/%s" % movie_id)
        relative = (movie_now.get("movieFile") or {}).get("relativePath") or "The.Matrix.1999.BluRay.mkv"
        video = "%s/%s" % (original, relative)
        sidecar_path = "%s/%s.en.srt" % (original, os.path.splitext(relative)[0])
        write_file(video)
        write_file(sidecar_path)
        return sidecar_path

    def move_upgrade(layout, expected_path):
        set_layout(layout)
        set_profile(call("GET", "/api/v3/qualityprofile/%s" % profile["id"]), "moveToDesignatedLocation", DEST)
        sidecar_path = seed_again()
        source = "/config/downloads/The.Matrix.1999.%s.mkv" % layout
        write_file(source)
        quality_id = 6 if layout == "flat" else 9
        import_file(movie_id, source, quality_id)
        moved = call("GET", "/api/v3/movie/%s" % movie_id)
        actual = moved["path"].rstrip("/")
        if actual != expected_path:
            raise SystemExit("%s upgrade path %s != %s" % (layout, actual, expected_path))
        if not exists("%s/The.Matrix.1999.%s.mkv" % (actual, layout)):
            raise SystemExit("upgrade file missing from %s" % actual)
        if exists(sidecar_path):
            raise SystemExit("old sidecar stayed at %s" % sidecar_path)
        print("moved upgrade for", layout, "to", actual)

    docker("mkdir -p %s && chown radarr:radarr %s" % (DEST, DEST))
    move_upgrade("flat", DEST)
    call("PUT", "/api/v3/movie/%s?moveFiles=false" % movie_id, {**call("GET", "/api/v3/movie/%s" % movie_id), "path": original})
    move_upgrade("movieFolder", "%s/%s" % (DEST, folder_name))
    print("upgrade import check passed")


if __name__ == "__main__":
    main()
