# Marshin386 Radarr fork

This fork adds flat and custom movie folder layouts. The design is in [custom-folder-layouts.md](custom-folder-layouts.md). The image is `ghcr.io/marshin386/radarr`.

## Versioning

Fork versions are the upstream Radarr release this tree is based on, plus a `-folders.N` suffix.

| Piece | Current value |
| --- | --- |
| Upstream base | Radarr **7.0.3** (`majorVersion` in `azure-pipelines.yml` on this `develop`; the last published upstream tag before that bump was `v7.0.2.10888`) |
| Fork revision | `folders.1` |
| Full version | `7.0.3-folders.1` |

`distribution/fork-version` is the only place that string is stored. The build stamps it as the assembly informational version, so System → About, the UI version, and the `X-Application-Version` header show `7.0.3-folders.1`. The numeric `AssemblyVersion` stays the upstream-style `10.0.0.*` placeholder because `System.Version` cannot hold a suffix.

Bump `N` for each fork release that does not move the upstream base (`7.0.3-folders.2`). When you merge a newer upstream Radarr, change the base too (`7.1.0-folders.1`) and reset `N` to 1.

## Cut a release

1. Set `distribution/fork-version` to the new version and merge it to the branch you are releasing.
2. Tag that commit. The tag name is the image tag:

   ```bash
   git tag v7.0.3-folders.1
   git push origin v7.0.3-folders.1
   ```

3. The [Docker image workflow](../.github/workflows/docker.yml) pushes `ghcr.io/marshin386/radarr:v7.0.3-folders.1`.

Do not force-push or delete `develop`. The repository ruleset rejects both.

## Image tags

| Git ref | Image tags |
| --- | --- |
| Any branch except `develop` (including this PR branch) | `testing`, the branch name (`/` becomes `-`), and `sha-<commit>` |
| `develop` | `latest` |
| Tag `v7.0.3-folders.1` | `v7.0.3-folders.1` |

To test this pull request after the workflow succeeds:

```bash
docker pull ghcr.io/marshin386/radarr:testing
```

## What you have to enable

The workflow uses `GITHUB_TOKEN` with `packages: write`. It runs on a push to the branch or tag, not only when the pull request page is opened.

If the run is missing under Actions:

1. On https://github.com/Marshin386/Radarr go to **Settings → Actions → General**.
2. Under **Actions permissions**, choose **Allow all actions and reusable workflows**.
3. Under **Workflow permissions**, choose **Read and write permissions**.
4. Save, then open the **Actions** tab, select **Docker image**, and run it with **Run workflow** on `cursor/custom-folder-layouts-d53d`.

If the push fails with `denied` or `insufficient_scope`:

1. **Settings → Actions → General → Workflow permissions → Read and write permissions**.
2. **Settings → Packages** (or the package page once it exists) and make sure Actions can publish. The first push creates `ghcr.io/marshin386/radarr`. A private package still pulls if you are logged in (`docker login ghcr.io`). To pull without logging in, open the package → **Package settings** → **Change visibility** → **Public**.

## Sync upstream later

Do this in a separate pull request, not mixed into a layout fix.

```bash
git remote add upstream https://github.com/Radarr/Radarr.git
git fetch upstream
git checkout develop
git checkout -b cursor/sync-upstream-folders
git merge upstream/develop
```

Resolve conflicts, keep `distribution/fork-version` and the folder-layout code, then set the version to the new upstream base with `-folders.1`. Open that branch as its own PR into `develop`. Do not force-push `develop`.

## Test container next to an existing Radarr

Back up the Radarr config you copy. Migration 244 adds columns and **cannot be rolled back** by starting the official image on the same database. Use a copy, and do not point this container at the live `/config`.

The container matches the usual Radarr image: port **7878** inside the container, app data in **`/config`**, and `PUID`, `PGID`, `TZ`, and `UMASK`. An existing config folder (the one that contains `config.xml` and `radarr.db`) mounts at `/config`. The app listens on the `Port` value in that `config.xml` (7878 unless you changed it). The image sets the update mechanism to Docker in the **copy** of `config.xml`, so this container does not replace itself with official Radarr.

For Portainer, paste [docker-compose.sample.yml](docker-compose.sample.yml) as a new stack. Do not edit the stack that already runs Radarr. The same file is below.

```yaml
# Portainer stack for the Marshin386 Radarr fork.
# Paste this whole file into Portainer → Stacks → Add stack.
# Name the stack something like radarr-test. Do not edit the stack that runs your real Radarr.
#
# Change every /path/to/... line before you deploy.
# PUID and PGID should be the user and group that own the files (the same IDs as your existing Radarr).
# TZ is an IANA zone name, for example America/New_York or Europe/London.
#
# Back up the copied config first. This image runs database migration 244, and that
# migration cannot be rolled back. Do not mount the live Radarr config folder, and do
# not point the official Radarr image at this copy afterward.
#
# The container listens on 7878. Host port 7879 keeps it off the existing Radarr port.
# If the copied config.xml sets Port to something other than 7878, change the right-hand
# side of the ports mapping to match.
#
# The image tag "testing" moves whenever a non-default branch is built. For a frozen
# test build, use the sha tag printed by the Docker image workflow instead.

services:
  radarr-test:
    image: ghcr.io/marshin386/radarr:testing
    container_name: radarr-test
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Etc/UTC
      # - UMASK=022
    ports:
      - "7879:7878"
    volumes:
      # Copy of the existing config directory (the folder that contains config.xml and radarr.db).
      - /path/to/radarr-test/config:/config
      # Movie library. Read-only so a rename or delete in the test UI cannot change files.
      - /path/to/movies:/movies:ro
      # Download client folder. Writable so you can try an import. Add :ro to keep it read-only.
      - /path/to/downloads:/downloads
    restart: unless-stopped

  # Production variant. Leave these lines commented while radarr-test is running.
  # Comment out radarr-test before uncommenting this service. Do not reuse the test
  # container name, host port, or config folder, and do not reuse your current Radarr's.
  # Pin a version tag from a git tag (docs/FORK.md). Do not use :testing for a library you keep.
  #
  # radarr:
  #   image: ghcr.io/marshin386/radarr:v7.0.3-folders.1
  #   container_name: radarr
  #   environment:
  #     - PUID=1000
  #     - PGID=1000
  #     - TZ=Etc/UTC
  #   ports:
  #     - "7878:7878"
  #   volumes:
  #     - /path/to/radarr/config:/config
  #     - /path/to/movies:/movies
  #     - /path/to/downloads:/downloads
  #   restart: unless-stopped
```

Open `http://<host>:7879`. About should show version `7.0.3-folders.1`. Add or edit a root folder and set its layout to **Flat** or **Custom nested folders** before importing into the copied library. Leave the production container on its own port and its own config.
