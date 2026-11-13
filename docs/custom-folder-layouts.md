# Custom and flat movie folder layouts

This document maps the places Radarr assumes one folder per movie, and describes the layout support added for [Radarr#153](https://github.com/Radarr/Radarr/issues/153).

Upstream declined the request because the Sonarr-derived model treats `Movie.Path` as a dedicated directory. Library import, disk scan, download import, renaming, deletion, extras, root-folder health, and free-space checks all follow from that. Closed attempts (Radarr#2380's `FlatFileName` column, Whisparr#165) only scanned files into the database and left rename, extras, and delete untouched. This design does not copy them.

## What people asked for

Issue #153 and the comments describe two libraries that the default layout cannot represent:

1. **Flat.** Many movie files sit directly in one root, for example `/movies/Nocturnal Animals (2016).mkv` and `/movies/Heat (1995).mkv`. Callers that browse the share (Samba, DLNA, a TV) do not want an extra folder click. Plex, Emby, and Kodi all accept this shape.
2. **Custom / nested.** Movies are grouped under arbitrary folders (collections, series, genres, languages) and several movies can share a group folder. The example from the issue is `/Theatre/Movie Series/Marvel Movies/(2008) Iron Man.mkv` next to a normal per-movie folder such as `/Theatre/Movies/Heat (1995)/Heat (1995).mkv`. A scan of `Movie Series` must not treat `Marvel Movies` as a single movie.

The default **movie folder** layout stays `{Root}/{Movie Title} ({Release Year})/{file}`. Existing installs keep that behavior.

## Where one folder per movie is assumed

| Area | Assumption | What breaks if it is ignored |
| --- | --- | --- |
| `Movie.Path` | The movie *is* its folder. `MovieFile.RelativePath` is relative to that folder. `FolderName()` returns `Path`. | Two movies cannot share a directory. Path equality is treated as a conflict. |
| `MoviePathValidator` | Path must be unique. | A second flat movie is rejected. |
| `MovieAncestorValidator` | Path must not contain another movie. | A group folder that also holds a per-movie subfolder is rejected. |
| `RootFolderValidator` | Path must not equal a root folder. | A flat movie, whose folder *is* the root, cannot be added. |
| `MoviePathBuilder` / `AddMovieService` | New movies always get `root + MovieFolderFormat`. | Downloads and the movie editor create a subfolder even for a flat root. |
| `RootFolderService.GetUnmappedFolders` | One level of subfolders (or `MovieFolderFormat` depth) is the list of movies to import. | Files in the root, and files buried under `Movie Series/Marvel Movies`, never appear. A group folder is offered as one movie. |
| `RootFolderService.GetBestRootFolderPath` | A root is only recognized with `IsParentPath`. Equal paths are not parents. | A flat movie whose path is the root resolves to the *parent* of the root. Disk scan, free space, and collection root then look at the wrong place. |
| `DiskScanService.Scan` | Every video under `Movie.Path` belongs to that movie. `ExistingFile` skips the sample/parse checks. Cleanup deletes DB rows that are not in that list. | Scanning one flat movie imports or drops every other file in the folder. |
| `ImportDecisionMaker` | `movie.Path.IsParentPath(file)` marks a library file. There is no "this file is a different movie" specification (`MatchesFolderSpecification` is a no-op). | Shared folders accept foreign files. |
| `DownloadedMovieImportService` | Refuses a download folder whose path equals some movie path, then moves the file to `movie.Path`. | Correct for a dedicated folder. For a flat movie the destination must be the shared root, and the file name is the only identity. |
| `MovieFileMovingService` / `FileNameBuilder.BuildFilePath` | Renames inside `movie.Path` and creates that folder. | Works for flat *if* `movie.Path` is the shared directory. Must not create a per-movie folder. |
| `RenameMovieFileService` | After rename, `RemoveEmptySubfolders(movie.Path)`. | Safe if it does not delete the shared directory itself. |
| `MoveMovieService` bulk move | `TransferFolder(source, root + movie folder)`. | Moving one flat movie would move the entire root. |
| `MediaFileDeletionService` movie delete | Deletes `movie.Path` unless another movie has the same path or lives underneath. On a match it aborts the whole batch (`return`). | Deleting one flat movie would remove every other file, or refuse and leave the file behind. The empty-root failsafe treats "no subdirectories" as an empty drive, which is exactly a flat root. |
| `MediaFileDeletionService` file delete | Sends the file to the recycle bin, then deletes `movie.Path` when `DeleteEmptyFolders` is on and the folder is empty. | Can delete the shared root after the last file of *this* movie is removed. |
| Extras (`SubtitleService`, `OtherExtraService`, `Existing*Importer`) | Anything in the movie folder that parses is that movie's extra. A folder with a single video also claims every subtitle. | In a flat folder, `Heat (1995).nfo` would attach to Nocturnal Animals. `poster.jpg` is ambiguous. |
| `ExtraFileService` delete | Deletes extras recorded for the movie file, using paths under `movie.Path`. | Correct per record, but folder delete was doing the real disk cleanup. Selective delete has to remove the matching sidecars itself. |
| Free space / `RootFolderCheck` | Space and missing-root checks use the best root of `movie.Path`. | Wrong root (see `GetBestRootFolderPath`) reports the wrong disk. The check itself does not assume a private folder once the root is correct. |
| `DiskSpaceService` | Groups movies by best root. | Same root-resolution bug. |
| Create empty movie folders | Missing `movie.Path` is created on scan. | Must not invent a per-movie directory for a flat root, and must not create the root when the root is what is missing. |

`MovieFile.RelativePath` is already the file location independent of how the folder was chosen. Flat and custom layouts keep that: the folder may be shared; the relative path (usually just the file name) identifies the movie's media and its sidecars.

## Data model

`MovieFolderLayout` on each root folder:

| Value | Meaning |
| --- | --- |
| `MovieFolder` (0) | Default. One dedicated folder per movie. Unchanged. |
| `Flat` (1) | Movie files live in the root. `Movie.Path` is the root. Many movies share it. |
| `Custom` (2) | A movie's folder is the directory that actually contains the file, at any depth. Several movies may share that directory. New movies (no path yet) use `NamingConfig.CustomMovieFolderFormat`. |

Migration `244`:

- `RootFolders.FolderLayout` integer, default `0`.
- `NamingConfig.CustomMovieFolderFormat` string. Existing rows are set to `{Movie Collection}/{Movie Title} ({Release Year})`. The column is unused until a root is `Custom`.

No movie-table rewrite and no unique index change. `Movies.Path` is indexed but not unique, so shared paths fit the current schema. Existing rows keep `FolderLayout = 0`.

`GetBestRootFolderPath` now treats a path equal to a root as that root. Dedicated movie folders still resolve by parent path, so current libraries do not move.

## Settings

- **Library Structure** is a naming setting (`folderLayout` on `NamingConfig`, edited under Settings → Media Management → Movie Naming). It applies to every root folder. Saving it copies the value onto `RootFolders.FolderLayout`, which is what path checks, import, rename, and move read. There is no separate per-root control. Changing the setting does not rewrite existing movie paths. New movies, imports, and moves use the new layout. Movie Folder Format is used only for Movie folder. Custom Movie Folder Format is used only for Custom nested folders. Flat uses neither.
- **Custom folder format** on the naming config (`customMovieFolderFormat`). Tokens are the movie-folder tokens, including `{Movie Collection}`. Slashes create nested directories. An empty format places new custom-layout movies in the root. The format does **not** have to contain the movie title, so `{Movie Collection}` can group a whole collection in one folder. It must not use file-quality tokens.
- The existing movie folder format is still what `MovieFolder` roots use.

## How a movie is stored

| Layout | `Movie.Path` | `MovieFile.RelativePath` | New movie path |
| --- | --- | --- | --- |
| Movie folder | Dedicated folder | Path under that folder | `root / MovieFolderFormat` |
| Flat | The root | File name in the root | The root |
| Custom, library import | Directory that contains the file (may be deep, may be shared) | File name in that directory | — |
| Custom, added without a path | `root / CustomMovieFolderFormat`, or the root when the format renders empty | File name | Built from the format |

Library import for `Flat` and `Custom` lists video files instead of child folders. Each item's path is the file's directory (the future `Movie.Path`), and the lookup term is parsed from the file name (`Heat (1995)`, not the scene junk). The relative path is the row id so two files with the same title in different groups do not collide. Files already stored for a movie, and any file inside a dedicated movie-folder movie, are hidden.

## Runtime behavior

**Disk scan / rescan.** Movie-folder scans are unchanged (recursive, every file in the folder). Flat and custom scans look only at the movie directory, then keep a file when:

- it is already this movie's file, or
- the parsed title matches this movie (title, original title, or alternative title, plus year when both sides have one), and it is not another movie's file, and it is not an ambiguous match for a second movie in the same directory.

Anything else stays on disk and stays out of this movie's row. Cleanup therefore cannot drop another movie's file from this movie, and cannot adopt it.

**Extras.** Movie-folder imports still take the folder. Shared layouts only take sidecars whose name equals the movie file stem or continues with `.`, `-`, or `_` (`Heat (1995).en.srt`, `Heat (1995)-poster.jpg`), or whose parsed title matches this movie and no other movie in the folder. A bare `poster.jpg` in a shared folder is left alone.

**Download import and rename.** The file is built with `BuildFilePath`, which writes into `Movie.Path`. For a flat root that is the root, so the download is renamed in place and no per-movie directory is created. Custom roots use the custom folder format for movies that do not already have a path. Rename of an existing flat file stays in the same directory.

**Delete.** Movie-folder deletes still remove the directory, with the same "this path is another movie's parent" abort. Flat and custom deletes remove only this movie's known files, video files that match this movie, and sidecars that match those stems. The shared directory is not removed. The relative paths are captured before the movie row is deleted so a parallel database cleanup cannot forget which file belonged to the movie. Deleting one file from a flat root no longer treats "no child directories" as an empty drive, and does not delete the root when empty-folder cleanup runs.

**Move.** The bulk editor retargets every selected movie to the current Library Structure, whether or not files are moved. Movie folder uses `<root>/<Movie Folder Format>`. Flat uses the root itself. Custom uses `<root>/<Custom Movie Folder Format>`, including nested folders. Choosing not to move files updates those paths only.

When files are moved between two dedicated folders, the whole movie folder moves, so subtitles, nfo files, and images go with it. Moving into or out of a flat root, or into a folder another movie already uses, moves only that movie's video files and sidecars whose names continue the video stem with `.`, `-`, or `_` (`Heat (1995).en.srt`). Other files, such as a bare `poster.jpg`, stay where they are. After that kind of move, a source folder that is empty, is not a root folder, and is not another movie's folder is deleted, and empty parent folders are removed up to but not including the root.

## Risks

- **Deleting the wrong files.** Mitigated by never deleting a shared directory, and by matching extras on a separator after the stem. `Heat (1995) Directors Cut.mkv` does not match stem `Heat (1995)` because the next character is a space. Two movies with the same parsed title and year in one folder are skipped as ambiguous rather than assigned twice.
- **A sidecar named like another movie.** `Heat (1995).nfo` is not attached to a different title. Unparsed junk (`poster.jpg`, `movie.nfo`) in a shared folder is not claimed.
- **Ancestor checks are relaxed for Flat and Custom.** A group folder may contain both loose files and a per-movie subfolder. Movie-folder roots keep the strict checks.
- **Switching a root that already has per-movie folders to Flat** does not rewrite existing paths. New movies go to the root; old folders remain until they are moved. Mixing those in one root is supported for Custom import, but a Movie-folder movie nested under a flat movie path is an awkward library and should be re-imported.
- **Custom format is not applied to files already on disk.** Import records the real directory. Organizing an existing nested library into the format is still a separate move.
- **Recursive custom import** walks the whole root. Large or network libraries will spend more time building the unmapped list than a one-level folder listing. The root-folder request still has the existing short timeout.
- **Extras in a subfolder of a shared directory** (`Subs/Heat.srt`) are not picked up. Movie-folder scans remain recursive.
- **Multi-part movies and extras that do not share the file stem** are unchanged for movie folders and are not claimed in a shared folder.
- **Empty-folder deletion** will not remove a shared directory, so an abandoned custom group folder can remain after its last movie is removed.

## What this version does not finish

- A migration that rewrites existing movie paths when the layout changes. Library Structure can be edited; paths stay where they are until a move.
- A per-root or per-movie layout override. Library Structure applies to every root folder.
- Applying the custom format to an already imported library (organize/move into `{Movie Collection}/...`).
- Parsing collection or genre from the parent folder name when importing, so new downloads can recreate `Movie Series/Marvel Movies` without a hand-written format.
- Subfolder extras, disc folders (`VIDEO_TS`, `BDMV`), and multi-file editions inside a shared directory.
- A warning in the UI when a flat delete is about to remove sidecars, beyond the existing delete confirmation.
- Making `MatchesFolderSpecification` reject a download whose parsed title is a different movie. Shared-folder scans filter before the decision engine; a manual import pointed at a shared folder can still target the selected movie.
