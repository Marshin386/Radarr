# Radarr (folder layouts fork)

[![Docker image](https://github.com/Marshin386/Radarr/actions/workflows/docker.yml/badge.svg?branch=develop)](https://github.com/Marshin386/Radarr/actions/workflows/docker.yml?query=branch%3Adevelop)
[![Version](https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fraw.githubusercontent.com%2FMarshin386%2FRadarr%2Fdevelop%2Fdistribution%2Ffork-version&search=%5E(%5B%5E%5Cs%5D%2B)&replace=%241&label=version)](https://github.com/Marshin386/Radarr/blob/develop/docs/FORK.md)
[![License: GPL-3.0](https://img.shields.io/github/license/Marshin386/Radarr)](https://github.com/Marshin386/Radarr/blob/develop/LICENSE)
[![Forked from Radarr/Radarr](https://img.shields.io/badge/forked%20from-Radarr%2FRadarr-blue?logo=github)](https://github.com/Radarr/Radarr)
[![GHCR](https://img.shields.io/badge/ghcr.io-marshin386%2Fradarr%3Alatest-blue?logo=github)](https://github.com/Marshin386/Radarr/pkgs/container/radarr)

A fork of [Radarr](https://github.com/Radarr/Radarr) that adds optional **flat** and **custom** movie folder layouts, while keeping the default one-folder-per-movie behavior.

## About this fork

This repository is a fork of [Radarr/Radarr](https://github.com/Radarr/Radarr), a movie collection manager for Usenet and BitTorrent users.

### What this fork adds

Each root folder can use one of three layouts:

- **Movie folder** (default): one dedicated folder per movie, the same as official Radarr.
- **Flat**: movie files live directly in the root, for example `/movies/Nocturnal Animals (2016).mkv` next to `/movies/Heat (1995).mkv`.
- **Custom**: existing movies can be imported from arbitrary nested folders, including several movies sharing a group folder such as `/Theatre/Movie Series/Marvel Movies/(2008) Iron Man.mkv`. New movies in that root use a custom folder format (default `{Movie Collection}/{Movie Title} ({Release Year})`).

When **Upgrades Allowed** is checked on a quality profile, **Upgraded File** chooses where the new file goes:

- **Replace in current location** (default): the upgrade replaces the file in the movie's current folder, the same as official Radarr.
- **Move to designated location**: the upgrade is stored under a chosen **Upgrade Destination** folder using the current Library Structure, and the movie path is updated to match.

Library import lists files instead of treating each subfolder as one movie. Disk scan and rescan only attach files that parse as that movie. Downloads and renames stay in the movie's folder (the root, for a flat library). Deleting a movie removes that movie's file and sidecars that match its filename, and does not delete a shared directory or other movies' files.

The fork version is shown in **System → About**. The published image is `ghcr.io/marshin386/radarr` (linux/amd64 and linux/arm64). More detail is in [docs/FORK.md](docs/FORK.md) and [docs/custom-folder-layouts.md](docs/custom-folder-layouts.md).

> [!WARNING]
> **Back up your Radarr config before switching**
>
> This fork upgrades the database (migrations 244–246). Starting official Radarr on that same database is not supported. Test with a copy of the config folder, and keep a backup of the original.

### Support for this fork

Problems with **flat or custom folder layouts**, **upgrade placement**, this Docker image, or other changes in this fork should be reported on [this repository's Issues](https://github.com/Marshin386/Radarr/issues), not on the official Radarr project. Please do not file fork-specific bugs against [Radarr/Radarr](https://github.com/Radarr/Radarr).

For general Radarr usage, official docs, and upstream bugs or feature requests, use the [Servarr wiki](https://wiki.servarr.com/radarr) and [official support channels](https://github.com/Radarr/Radarr#support).

## Quick start

The usual image for this fork is `ghcr.io/marshin386/radarr:latest`, built from the default `develop` branch. Replace the placeholder paths, `PUID`, `PGID`, and `TZ` before you start the stack.

```yaml
services:
  radarr:
    image: ghcr.io/marshin386/radarr:latest
    container_name: radarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
    ports:
      - "7878:7878"
    volumes:
      - /path/to/config:/config
      - /path/to/movies:/movies
      - /path/to/downloads:/downloads
    restart: unless-stopped
```

Then open `http://<host>:7878`. When you add a root folder, choose **Movie folder**, **Flat**, or **Custom** as the layout.

| Image tag | When to use it |
| --- | --- |
| `latest` | Default branch (`develop`) |
| `sha-<commit>` | Pin a specific build |
| `testing` | Non-default branches only |
| `v*` | A tagged release, once one is cut |

Before you start, back up the config folder and use a **copy** if you still need official Radarr. A sample test stack (host port 7879, so it can run beside an existing instance) is in [docs/docker-compose.sample.yml](docs/docker-compose.sample.yml).

---

## Upstream Radarr

The rest of this document is from the official [Radarr](https://github.com/Radarr/Radarr) project, kept here as a reference. For current docs, see the [Servarr wiki](https://wiki.servarr.com/radarr). This fork is not affiliated with the Radarr team.

Radarr is a movie collection manager for Usenet and BitTorrent users. It can monitor multiple RSS feeds for new movies and will interface with clients and indexers to grab, sort, and rename them. It can also be configured to automatically upgrade the quality of existing files in the library when a better quality format becomes available.
Note that only one type of a given movie is supported. If you want both a 4k version and 1080p version of a given movie you will need multiple instances.

### Major Features Include

* Adding new movies with lots of information, such as trailers, ratings, etc.
* Support for major platforms: Windows, Linux, macOS, Raspberry Pi, etc.
* Can watch for better quality of the movies you have and do an automatic upgrade. _eg. from DVD to Blu-Ray_
* Automatic failed download handling will try another release if one fails
* Manual search so you can pick any release or to see why a release was not downloaded automatically
* Full integration with SABnzbd and NZBGet
* Automatically searching for releases as well as RSS Sync
* Automatically importing downloaded movies
* Recognizing Special Editions, Director's Cut, etc.
* Identifying releases with hardcoded subs
* Identifying releases with AKA movie names
* SABnzbd, NZBGet, QBittorrent, Deluge, rTorrent, Transmission, uTorrent, and other download clients are supported and integrated
* Full integration with Kodi and Plex (notifications, library updates)
* Importing Metadata such as trailers or subtitles
* Adding metadata such as posters and information for Kodi and others to use
* Advanced customization for profiles, such that Radarr will always download the copy you want
* A beautiful UI

### Official support and docs

[![Wiki](https://img.shields.io/badge/servarr-wiki-181717.svg?maxAge=60)](https://wiki.servarr.com/radarr)
[![Discord](https://img.shields.io/badge/discord-chat-7289DA.svg?maxAge=60)](https://radarr.video/discord)

Note: GitHub Issues on the official project are for Bugs and Feature Requests Only. Do not use them for issues that are specific to this fork.

[![GitHub - Bugs and Feature Requests Only](https://img.shields.io/badge/github-issues-red.svg?maxAge=60)](https://github.com/Radarr/Radarr/issues)

### Contributors & Developers

[API Documentation](https://radarr.video/docs/api/)

This project exists thanks to all the people who contribute.

- [Contribute (GitHub)](CONTRIBUTING.md)
- [Contribution (Wiki Article)](https://wiki.servarr.com/radarr/contributing)
- [Official contributors](https://github.com/Radarr/Radarr/graphs/contributors)

### License

* [GNU GPL v3](http://www.gnu.org/licenses/gpl.html)
* Copyright 2010-2025
