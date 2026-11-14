using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        MovieFileMoveResult UpgradeMovieFile(MovieFile movieFile, LocalMovie localMovie, bool copyOnly = false);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveMovieFiles _movieFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly IBuildMoviePaths _moviePathBuilder;
        private readonly IMovieService _movieService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IKeptMovieFileRepository _keptMovieFileRepository;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMoveMovieFiles movieFileMover,
                                       IDiskProvider diskProvider,
                                       IBuildMoviePaths moviePathBuilder,
                                       IMovieService movieService,
                                       IRootFolderService rootFolderService,
                                       IKeptMovieFileRepository keptMovieFileRepository,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _movieFileMover = movieFileMover;
            _diskProvider = diskProvider;
            _moviePathBuilder = moviePathBuilder;
            _movieService = movieService;
            _rootFolderService = rootFolderService;
            _keptMovieFileRepository = keptMovieFileRepository;
            _logger = logger;
        }

        public MovieFileMoveResult UpgradeMovieFile(MovieFile movieFile, LocalMovie localMovie, bool copyOnly = false)
        {
            _logger.Trace("Upgrading movie file.");

            var moveFileResult = new MovieFileMoveResult();

            var existingFile = localMovie.Movie.MovieFileId > 0 ? localMovie.Movie.MovieFile : null;
            var oldMoviePath = localMovie.Movie.Path;
            var newMoviePath = GetUpgradePath(localMovie.Movie, existingFile);

            var rootFolder = _diskProvider.GetParentFolder(oldMoviePath);

            // If there are existing movie files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFile != null && !_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolder}' was not found.");
            }

            if (existingFile != null)
            {
                var movieFilePath = Path.Combine(oldMoviePath, existingFile.RelativePath);
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(movieFilePath));
                string recycleBinPath = null;
                var keepOriginal = ShouldKeepOriginalFile(localMovie.Movie.QualityProfile);
                var fileExists = _diskProvider.FileExists(movieFilePath);

                if (keepOriginal)
                {
                    if (fileExists)
                    {
                        _logger.Info("Keeping existing movie file on disk, untracked: {0}", movieFilePath);
                        _keptMovieFileRepository.Remember(localMovie.Movie.Id, movieFilePath);
                    }
                    else
                    {
                        _logger.Warn("Existing movie file missing from disk: {0}", movieFilePath);
                    }

                    moveFileResult.OldFiles.Add(new DeletedMovieFile(existingFile, null));
                    _mediaFileService.Delete(existingFile, DeleteMediaFileReason.UpgradeKept);
                }
                else
                {
                    if (fileExists)
                    {
                        _logger.Debug("Removing existing movie file: {0}", existingFile);
                        recycleBinPath = _recycleBinProvider.DeleteFile(movieFilePath, subfolder);
                        RemoveMatchingSidecars(movieFilePath, subfolder);
                    }
                    else
                    {
                        _logger.Warn("Existing movie file missing from disk: {0}", movieFilePath);
                    }

                    moveFileResult.OldFiles.Add(new DeletedMovieFile(existingFile, recycleBinPath));
                    _mediaFileService.Delete(existingFile, DeleteMediaFileReason.Upgrade);
                }
            }

            if (newMoviePath.IsNotNullOrWhiteSpace() && !newMoviePath.PathEquals(oldMoviePath))
            {
                _logger.Info("Moving upgraded movie from '{0}' to '{1}'", oldMoviePath, newMoviePath);
                localMovie.Movie.Path = newMoviePath;
                _movieService.UpdateMovie(localMovie.Movie);
                RemoveEmptySourceFolders(localMovie.Movie, oldMoviePath);
            }

            localMovie.OldFiles = moveFileResult.OldFiles;

            if (copyOnly)
            {
                moveFileResult.MovieFile = _movieFileMover.CopyMovieFile(movieFile, localMovie);
            }
            else
            {
                moveFileResult.MovieFile = _movieFileMover.MoveMovieFile(movieFile, localMovie);
            }

            return moveFileResult;
        }

        private string GetUpgradePath(Movie movie, MovieFile existingFile)
        {
            var profile = movie.QualityProfile;

            if (existingFile == null ||
                profile == null ||
                !profile.UpgradeAllowed ||
                profile.UpgradeFilePlacement != UpgradeFilePlacement.MoveToDesignatedLocation ||
                profile.UpgradeDestination.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (!_diskProvider.FolderExists(profile.UpgradeDestination))
            {
                throw new RootFolderNotFoundException($"Upgrade destination '{profile.UpgradeDestination}' was not found.");
            }

            return _moviePathBuilder.BuildLibraryPath(movie, profile.UpgradeDestination);
        }

        private static bool ShouldKeepOriginalFile(QualityProfile profile)
        {
            return profile != null &&
                   profile.KeepOriginalFile &&
                   profile.UpgradeAllowed &&
                   profile.UpgradeFilePlacement == UpgradeFilePlacement.MoveToDesignatedLocation &&
                   profile.UpgradeDestination.IsNotNullOrWhiteSpace();
        }

        private void RemoveMatchingSidecars(string movieFilePath, string subfolder)
        {
            var directory = _diskProvider.GetParentFolder(movieFilePath);
            var stem = Path.GetFileNameWithoutExtension(movieFilePath);

            foreach (var extra in _diskProvider.GetFiles(directory, false) ?? Enumerable.Empty<string>())
            {
                if (MediaFileExtensions.Extensions.Contains(Path.GetExtension(extra)))
                {
                    continue;
                }

                if (!MovieFileLayout.ExtraFileMatchesMovieStem(extra, stem) || !_diskProvider.FileExists(extra))
                {
                    continue;
                }

                _recycleBinProvider.DeleteFile(extra, subfolder);
            }
        }

        private void RemoveEmptySourceFolders(Movie movie, string sourcePath)
        {
            var root = _rootFolderService.GetBestRootFolderPath(sourcePath);
            var paths = _movieService.AllMoviePaths() ?? new Dictionary<int, string>();
            var current = sourcePath;

            while (current.IsNotNullOrWhiteSpace())
            {
                if (root.IsNotNullOrWhiteSpace() && current.PathEquals(root))
                {
                    return;
                }

                if (paths.Any(entry => entry.Key != movie.Id &&
                                       entry.Value.IsNotNullOrWhiteSpace() &&
                                       (entry.Value.PathEquals(current) || current.IsParentPath(entry.Value))))
                {
                    return;
                }

                if (!_diskProvider.FolderExists(current))
                {
                    current = current.GetParentPath();
                    continue;
                }

                var files = _diskProvider.GetFiles(current, false) ?? Enumerable.Empty<string>();
                var directories = _diskProvider.GetDirectories(current) ?? Enumerable.Empty<string>();

                if (files.Any() || directories.Any())
                {
                    return;
                }

                _diskProvider.DeleteFolder(current, false);
                current = current.GetParentPath();
            }
        }
    }
}
