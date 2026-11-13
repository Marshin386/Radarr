#!/usr/bin/env python3
"""Bulk-edit two movies onto a new root for each library structure, with and without moving files."""
import json
import os
import subprocess
import time
import urllib.error
import urllib.request

BASE = os.environ.get("RADARR_URL", "http://127.0.0.1:7878")
KEY = os.environ["RADARR_API_KEY"]
CONTAINER = os.environ.get("RADARR_CONTAINER", "radarr-smoke")
ROOT_A = "/config/bulk-a"
ROOT_B = "/config/bulk-b"
TMDB_IDS = (603, 680)  # The Matrix, Pulp Fiction


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


def docker(*args):
    subprocess.check_call(["docker", "exec", CONTAINER, "sh", "-c", " ".join(args)])


def exists(path):
    result = subprocess.run(
        ["docker", "exec", CONTAINER, "sh", "-c", "test -e %s" % shell_quote(path)],
        check=False,
    )
    return result.returncode == 0


def shell_quote(value):
    return "'" + value.replace("'", "'\"'\"'") + "'"


def wait_for_commands():
    for _ in range(60):
        commands = call("GET", "/api/v3/command") or []
        busy = [
            command
            for command in commands
            if command.get("status") not in ("completed", "failed", "aborted")
        ]
        if not busy:
            failed = [command for command in commands if command.get("status") == "failed"]
            if failed:
                raise SystemExit("a command failed: %s" % failed[-1])
            return
        time.sleep(1)
    raise SystemExit("commands did not finish")


def set_layout(layout, custom_format=None):
    naming = call("GET", "/api/v3/config/naming")
    naming["folderLayout"] = layout
    naming["movieFolderFormat"] = "{Movie Title} ({Release Year})"
    if custom_format:
        naming["customMovieFolderFormat"] = custom_format
    call("PUT", "/api/v3/config/naming/%s" % naming["id"], naming)


def editor(movie_ids, root, move_files):
    return call(
        "PUT",
        "/api/v3/movie/editor",
        {"movieIds": movie_ids, "rootFolderPath": root, "moveFiles": move_files},
    )


def restore_paths(originals):
    for movie_id, path in originals.items():
        movie = call("GET", "/api/v3/movie/%s" % movie_id)
        movie["path"] = path
        call("PUT", "/api/v3/movie/%s?moveFiles=false" % movie_id, movie)


def plant(path):
    name = os.path.basename(path)
    docker(
        "mkdir",
        "-p",
        shell_quote(path),
        "&&",
        "printf",
        "x",
        ">",
        shell_quote("%s/%s.mkv" % (path, name)),
        "&&",
        "printf",
        "x",
        ">",
        shell_quote("%s/%s.en.srt" % (path, name)),
        "&&",
        "printf",
        "x",
        ">",
        shell_quote("%s/poster.jpg" % path),
        "&&",
        "chown",
        "-R",
        "radarr:radarr",
        shell_quote(path),
    )


def reset(originals):
    restore_paths(originals)
    for path in originals.values():
        plant(path)


def file_at(folder, name):
    return "%s/%s" % (folder.rstrip("/"), name)


def check_case(layout, movies, originals, destination, move_files):
    print("bulk %s moveFiles=%s -> %s" % (layout, move_files, destination))
    reset(originals)
    updated = editor([movie["id"] for movie in movies], destination, move_files)
    if move_files:
        wait_for_commands()

    by_id = {movie["id"]: movie for movie in updated}
    for movie in movies:
        original = originals[movie["id"]]
        folder_name = os.path.basename(original)
        current = by_id[movie["id"]]["path"].rstrip("/")
        video = "%s.mkv" % folder_name
        subtitle = "%s.en.srt" % folder_name

        if layout == "flat":
            if current != destination:
                raise SystemExit("flat path %s is not the root %s" % (current, destination))
        elif layout == "movieFolder":
            expected = "%s/%s" % (destination, folder_name)
            if current != expected:
                raise SystemExit("movie-folder path %s != %s" % (current, expected))
        else:
            if not current.startswith(destination + "/Group/"):
                raise SystemExit("custom path %s is not under %s/Group" % (current, destination))

        if move_files:
            if not exists(file_at(current, video)) or not exists(file_at(current, subtitle)):
                raise SystemExit("moved files missing from %s" % current)
            if layout == "flat":
                if not exists(file_at(original, "poster.jpg")):
                    raise SystemExit("unrelated poster.jpg should stay in %s" % original)
                if exists(file_at(original, video)):
                    raise SystemExit("video should have left %s" % original)
            elif layout == "movieFolder":
                if not exists(file_at(current, "poster.jpg")):
                    raise SystemExit("poster.jpg should move with the movie folder")
                if exists(original):
                    raise SystemExit("old movie folder should have been moved away: %s" % original)
        else:
            if not exists(file_at(original, video)):
                raise SystemExit("file moved even though moveFiles was false: %s" % original)
            if exists(file_at(current, video)) and current != original:
                raise SystemExit("file appeared at the new path without a move: %s" % current)


def main():
    for root in (ROOT_A, ROOT_B):
        docker("mkdir", "-p", root, "&&", "chown", "radarr:radarr", root)
        call("POST", "/api/v3/rootfolder", {"path": root})

    set_layout("movieFolder")
    profiles = call("GET", "/api/v3/qualityprofile")
    profile_id = profiles[0]["id"]
    movies = []
    for tmdb_id in TMDB_IDS:
        movie = call(
            "POST",
            "/api/v3/movie",
            {
                "tmdbId": tmdb_id,
                "qualityProfileId": profile_id,
                "rootFolderPath": ROOT_A,
                "monitored": True,
                "minimumAvailability": "released",
                "addOptions": {"searchForMovie": False},
            },
        )
        movies.append(movie)
        print("added", movie.get("title"), movie.get("path"))

    originals = {movie["id"]: movie["path"].rstrip("/") for movie in movies}
    if len(set(originals.values())) != 2:
        raise SystemExit("the two movies did not get distinct folders: %s" % originals)

    for layout, custom in (
        ("movieFolder", None),
        ("flat", None),
        ("custom", "Group/{Movie Title} ({Release Year})"),
    ):
        set_layout(layout, custom)
        check_case(layout, movies, originals, ROOT_B, False)
        check_case(layout, movies, originals, ROOT_B, True)

    print("bulk root move check passed")


if __name__ == "__main__":
    main()
