using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteMovieFile(Movie movie, MovieFile movieFile);
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandleAsync<MoviesDeletedEvent>,
                                            IHandle<MovieFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IMovieLayoutService _movieLayoutService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IMovieService movieService,
                                        IMovieLayoutService movieLayoutService,
                                        IRootFolderService rootFolderService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _movieLayoutService = movieLayoutService;
            _rootFolderService = rootFolderService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void DeleteMovieFile(Movie movie, MovieFile movieFile)
        {
            var fullPath = Path.Combine(movie.Path, movieFile.RelativePath);
            var rootFolder = _diskProvider.GetParentFolder(movie.Path);
            var bestRoot = _rootFolderService.GetBestRootFolderPath(movie.Path);

            if (_movieLayoutService.GetLayout(movie) != MovieFolderLayout.MovieFolder &&
                bestRoot.IsNotNullOrWhiteSpace() &&
                movie.Path.PathEquals(bestRoot))
            {
                rootFolder = movie.Path;
            }

            if (!_diskProvider.FolderExists(rootFolder))
            {
                _logger.Warn("Movie's root folder ({0}) doesn't exist.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) doesn't exist.", rootFolder);
            }

            if (RootAppearsEmpty(rootFolder))
            {
                _logger.Warn("Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
            }

            if (_diskProvider.FolderExists(movie.Path) && _diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting movie file: {0}", fullPath);

                var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));

                try
                {
                    _recycleBinProvider.DeleteFile(fullPath, subfolder);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to delete movie file");
                    throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete movie file");
                }
            }

            // Delete the movie file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(movieFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var allMovies = _movieService.AllMoviePaths();

                foreach (var movie in message.Movies)
                {
                    if (_movieLayoutService.GetLayout(movie) != MovieFolderLayout.MovieFolder)
                    {
                        var ownedPaths = message.OwnedRelativePaths != null && message.OwnedRelativePaths.TryGetValue(movie.Id, out var paths)
                            ? paths
                            : null;
                        DeleteOwnedFiles(movie, ownedPaths);
                        continue;
                    }

                    foreach (var s in allMovies)
                    {
                        if (s.Key == movie.Id)
                        {
                            continue;
                        }

                        if (movie.Path.IsParentPath(s.Value))
                        {
                            _logger.Error("Movie path: '{0}' is a parent of another movie, not deleting files.", movie.Path);
                            return;
                        }

                        if (movie.Path.PathEquals(s.Value))
                        {
                            _logger.Error("Movie path: '{0}' is the same as another movie, not deleting files.", movie.Path);
                            return;
                        }
                    }

                    if (_diskProvider.FolderExists(movie.Path))
                    {
                        _recycleBinProvider.DeleteFolder(movie.Path);
                    }
                }

                _eventAggregator.PublishEvent(new DeleteCompletedEvent());
            }
        }

        private void DeleteOwnedFiles(Movie movie, IEnumerable<string> ownedRelativePaths)
        {
            var relatives = new List<string>(ownedRelativePaths ?? Enumerable.Empty<string>());

            if (movie.MovieFile?.RelativePath.IsNotNullOrWhiteSpace() == true)
            {
                relatives.Add(movie.MovieFile.RelativePath);
            }

            var databaseFiles = _mediaFileService.GetFilesByMovie(movie.Id);

            if (databaseFiles != null)
            {
                relatives.AddRange(databaseFiles.Select(file => file.RelativePath));
            }

            if (_diskProvider.FolderExists(movie.Path))
            {
                var videos = (_diskProvider.GetFiles(movie.Path, false) ?? Enumerable.Empty<string>())
                    .Where(file => MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                    .ToList();

                foreach (var match in _movieLayoutService.SelectVideoFiles(movie, videos))
                {
                    if (movie.Path.IsParentPath(match))
                    {
                        relatives.Add(movie.Path.GetRelativePath(match));
                    }
                }
            }

            foreach (var path in _movieLayoutService.GetDeletableFilePaths(movie, relatives))
            {
                if (!_diskProvider.FileExists(path))
                {
                    continue;
                }

                _logger.Info("Deleting movie file: {0}", path);

                try
                {
                    _recycleBinProvider.DeleteFile(path, RecycleBinSubfolder(movie.Path, path));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to delete movie file: {0}", path);
                }
            }
        }

        private string RecycleBinSubfolder(string moviePath, string filePath)
        {
            var rootParent = _diskProvider.GetParentFolder(moviePath);
            var fileParent = _diskProvider.GetParentFolder(filePath);

            if (rootParent.IsNullOrWhiteSpace() || fileParent.IsNullOrWhiteSpace() || !rootParent.IsParentPath(fileParent))
            {
                return string.Empty;
            }

            return rootParent.GetRelativePath(fileParent);
        }

        private bool RootAppearsEmpty(string rootFolder)
        {
            var directories = _diskProvider.GetDirectories(rootFolder);

            if (directories != null && directories.Any())
            {
                return false;
            }

            var files = _diskProvider.GetFiles(rootFolder, false);

            return files == null || !files.Any();
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(MovieFileDeletedEvent message)
        {
            if (_configService.DeleteEmptyFolders)
            {
                var movie = message.MovieFile.Movie;
                var moviePath = movie.Path;
                var rootFolder = _rootFolderService.GetBestRootFolderPath(moviePath);
                var layout = _movieLayoutService.GetLayout(movie);

                if (layout != MovieFolderLayout.MovieFolder || (rootFolder.IsNotNullOrWhiteSpace() && moviePath.PathEquals(rootFolder)))
                {
                    if (_diskProvider.FolderExists(moviePath))
                    {
                        _diskProvider.RemoveEmptySubfolders(moviePath);
                    }

                    return;
                }

                var folder = message.MovieFile.Path.GetParentPath();

                while (moviePath.IsParentPath(folder))
                {
                    if (_diskProvider.FolderExists(folder))
                    {
                        _diskProvider.RemoveEmptySubfolders(folder);
                    }

                    folder = folder.GetParentPath();
                }

                _diskProvider.RemoveEmptySubfolders(moviePath);

                if (_diskProvider.FolderEmpty(moviePath))
                {
                    _diskProvider.DeleteFolder(moviePath, true);
                }
            }
        }
    }
}
