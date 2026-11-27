using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Movies
{
    public interface IMovieLayoutService
    {
        MovieFolderLayout GetLayout(string path);
        MovieFolderLayout GetLayout(Movie movie);
        List<string> SelectVideoFiles(Movie movie, List<string> filesOnDisk);
        List<string> SelectExtraFiles(Movie movie, List<string> extraFiles, IEnumerable<string> videoFilesForMovie);
        IReadOnlyList<string> GetDeletableFilePaths(Movie movie, IEnumerable<string> movieFileRelativePaths);
    }

    public class MovieLayoutService : IMovieLayoutService,
                                      IHandle<MovieFileAddedEvent>,
                                      IHandle<MovieFileDeletedEvent>,
                                      IHandle<MovieFileRenamedEvent>,
                                      IHandle<MoviesDeletedEvent>
    {
        private const int SharedFolderCacheLimit = 8;
        private static readonly TimeSpan SharedFolderCacheTtl = TimeSpan.FromMinutes(2);

        private readonly IRootFolderService _rootFolderService;
        private readonly IMovieRepository _movieRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IKeptMovieFileRepository _keptMovieFileRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly object _folderMoviesGate = new object();
        private readonly Dictionary<string, SharedFolderSnapshot> _sharedFolders = new Dictionary<string, SharedFolderSnapshot>(PathEqualityComparer.Instance);

        public MovieLayoutService(IRootFolderService rootFolderService,
                                  IMovieRepository movieRepository,
                                  IMediaFileService mediaFileService,
                                  IKeptMovieFileRepository keptMovieFileRepository,
                                  IDiskProvider diskProvider,
                                  Logger logger)
        {
            _rootFolderService = rootFolderService;
            _movieRepository = movieRepository;
            _mediaFileService = mediaFileService;
            _keptMovieFileRepository = keptMovieFileRepository;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public MovieFolderLayout GetLayout(string path)
        {
            return _rootFolderService.GetFolderLayout(path);
        }

        public MovieFolderLayout GetLayout(Movie movie)
        {
            return GetLayout(movie?.Path);
        }

        public List<string> SelectVideoFiles(Movie movie, List<string> filesOnDisk)
        {
            if (movie == null || GetLayout(movie) == MovieFolderLayout.MovieFolder)
            {
                return filesOnDisk;
            }

            var ownedByThisMovie = new HashSet<string>(PathEqualityComparer.Instance);
            foreach (var movieFile in _mediaFileService.GetFilesByMovie(movie.Id) ?? new List<MovieFile>())
            {
                if (movieFile.RelativePath.IsNotNullOrWhiteSpace())
                {
                    ownedByThisMovie.Add(Path.Combine(movie.Path, movieFile.RelativePath));
                }
            }

            var ownedByOthers = OtherMovieFilePaths(movie);
            var otherMovies = OtherMoviesSharingPath(movie);
            var keptPaths = KeptPaths(movie);
            var selected = new List<string>();

            foreach (var file in filesOnDisk)
            {
                if (ownedByThisMovie.Contains(file))
                {
                    selected.Add(file);
                    continue;
                }

                if (keptPaths.Contains(file))
                {
                    _logger.Debug("Skipping kept original file: {0}", file);
                    continue;
                }

                if (ownedByOthers.Contains(file))
                {
                    continue;
                }

                if (!MovieFileLayout.FileMatchesMovie(movie, file))
                {
                    continue;
                }

                if (otherMovies.Any(other => MovieFileLayout.FileMatchesMovie(other, file)))
                {
                    _logger.Debug("Skipping {0}: it matches more than one movie in {1}", file, movie.Path);
                    continue;
                }

                selected.Add(file);
            }

            return selected;
        }

        public List<string> SelectExtraFiles(Movie movie, List<string> extraFiles, IEnumerable<string> videoFilesForMovie)
        {
            if (movie == null || GetLayout(movie) == MovieFolderLayout.MovieFolder)
            {
                return extraFiles;
            }

            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var videoFile in videoFilesForMovie ?? Enumerable.Empty<string>())
            {
                var stem = Path.GetFileNameWithoutExtension(videoFile);

                if (stem.IsNotNullOrWhiteSpace())
                {
                    stems.Add(stem);
                }
            }

            foreach (var movieFile in _mediaFileService.GetFilesByMovie(movie.Id) ?? new List<MovieFile>())
            {
                var stem = Path.GetFileNameWithoutExtension(movieFile.RelativePath);

                if (stem.IsNotNullOrWhiteSpace())
                {
                    stems.Add(stem);
                }
            }

            var otherMovies = OtherMoviesSharingPath(movie);

            return extraFiles.Where(file =>
            {
                if (stems.Any(stem => MovieFileLayout.ExtraFileMatchesMovieStem(file, stem)))
                {
                    return true;
                }

                if (!MovieFileLayout.FileMatchesMovie(movie, file))
                {
                    return false;
                }

                return !otherMovies.Any(other => MovieFileLayout.FileMatchesMovie(other, file));
            }).ToList();
        }

        public IReadOnlyList<string> GetDeletableFilePaths(Movie movie, IEnumerable<string> movieFileRelativePaths)
        {
            var paths = new List<string>();
            var stems = new List<string>();

            foreach (var relativePath in movieFileRelativePaths ?? Enumerable.Empty<string>())
            {
                if (relativePath.IsNullOrWhiteSpace() || movie?.Path.IsNullOrWhiteSpace() != false)
                {
                    continue;
                }

                paths.Add(Path.Combine(movie.Path, relativePath));

                var stem = Path.GetFileNameWithoutExtension(relativePath);

                if (stem.IsNotNullOrWhiteSpace())
                {
                    stems.Add(stem);
                }
            }

            if (movie?.Path.IsNotNullOrWhiteSpace() == true && _diskProvider.FolderExists(movie.Path))
            {
                foreach (var file in _diskProvider.GetFiles(movie.Path, false) ?? Enumerable.Empty<string>())
                {
                    if (MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    if (stems.Any(stem => MovieFileLayout.ExtraFileMatchesMovieStem(file, stem)))
                    {
                        paths.Add(file);
                    }
                }
            }

            return paths.Distinct(PathEqualityComparer.Instance).ToList();
        }

        public void Handle(MovieFileAddedEvent message)
        {
            InvalidateFolder(message?.MovieFile?.Movie?.Path);
        }

        public void Handle(MovieFileDeletedEvent message)
        {
            InvalidateFolder(message?.MovieFile?.Movie?.Path);
        }

        public void Handle(MovieFileRenamedEvent message)
        {
            InvalidateFolder(message?.Movie?.Path);
        }

        public void Handle(MoviesDeletedEvent message)
        {
            if (message?.Movies == null)
            {
                return;
            }

            foreach (var movie in message.Movies)
            {
                InvalidateFolder(movie?.Path);
            }
        }

        private HashSet<string> KeptPaths(Movie movie)
        {
            var kept = movie == null ? null : _keptMovieFileRepository.GetByMovieId(movie.Id);

            return new HashSet<string>(
                (kept ?? new List<KeptMovieFile>()).Select(file => file.Path).Where(path => path.IsNotNullOrWhiteSpace()),
                PathEqualityComparer.Instance);
        }

        private HashSet<string> OtherMovieFilePaths(Movie movie)
        {
            var paths = new HashSet<string>(PathEqualityComparer.Instance);
            var snapshot = GetSnapshot(movie?.Path);

            foreach (var pair in snapshot.FilesByMovie)
            {
                if (movie != null && pair.Key == movie.Id)
                {
                    continue;
                }

                foreach (var path in pair.Value)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private List<Movie> OtherMoviesSharingPath(Movie movie)
        {
            return GetSnapshot(movie?.Path).Movies.Where(other => other.Id != movie.Id).ToList();
        }

        private SharedFolderSnapshot GetSnapshot(string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return SharedFolderSnapshot.Empty;
            }

            lock (_folderMoviesGate)
            {
                if (_sharedFolders.TryGetValue(path, out var cached) && DateTime.UtcNow - cached.Utc < SharedFolderCacheTtl)
                {
                    return cached;
                }
            }

            var snapshot = LoadSnapshot(path);

            lock (_folderMoviesGate)
            {
                if (_sharedFolders.TryGetValue(path, out var cached) && DateTime.UtcNow - cached.Utc < SharedFolderCacheTtl)
                {
                    return cached;
                }

                _sharedFolders[path] = snapshot;
                TrimSharedFolders(path);
                return snapshot;
            }
        }

        private SharedFolderSnapshot LoadSnapshot(string path)
        {
            var ids = (_movieRepository.AllMoviePaths() ?? new Dictionary<int, string>())
                .Where(entry => entry.Value.IsNotNullOrWhiteSpace() && entry.Value.PathEquals(path))
                .Select(entry => entry.Key)
                .ToList();

            var movies = ids.Count == 0
                ? new List<Movie>()
                : _movieRepository.Get(ids)?.ToList() ?? new List<Movie>();

            var byId = new Dictionary<int, Movie>();

            foreach (var movie in movies)
            {
                byId[movie.Id] = movie;
            }

            var filesByMovie = new Dictionary<int, HashSet<string>>();

            if (byId.Count > 0)
            {
                foreach (var file in _mediaFileService.GetRelativePathsByMovies(byId.Keys) ?? new List<MovieFilePath>())
                {
                    if (file == null || file.RelativePath.IsNullOrWhiteSpace() || !byId.TryGetValue(file.MovieId, out var owner) || owner.Path.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    if (!filesByMovie.TryGetValue(file.MovieId, out var paths))
                    {
                        paths = new HashSet<string>(PathEqualityComparer.Instance);
                        filesByMovie[file.MovieId] = paths;
                    }

                    paths.Add(Path.Combine(owner.Path, file.RelativePath));
                }
            }

            return new SharedFolderSnapshot
            {
                Movies = movies,
                FilesByMovie = filesByMovie,
                Utc = DateTime.UtcNow
            };
        }

        private void TrimSharedFolders(string newestPath)
        {
            if (_sharedFolders.Count <= SharedFolderCacheLimit)
            {
                return;
            }

            string oldestKey = null;
            var oldestUtc = DateTime.MaxValue;

            foreach (var entry in _sharedFolders)
            {
                if (entry.Key.PathEquals(newestPath))
                {
                    continue;
                }

                if (entry.Value.Utc < oldestUtc)
                {
                    oldestUtc = entry.Value.Utc;
                    oldestKey = entry.Key;
                }
            }

            if (oldestKey != null)
            {
                _sharedFolders.Remove(oldestKey);
            }
        }

        private void InvalidateFolder(string path)
        {
            lock (_folderMoviesGate)
            {
                if (path.IsNullOrWhiteSpace())
                {
                    _sharedFolders.Clear();
                    return;
                }

                _sharedFolders.Remove(path);
            }
        }

        private sealed class SharedFolderSnapshot
        {
            public static readonly SharedFolderSnapshot Empty = new SharedFolderSnapshot
            {
                Movies = new List<Movie>(),
                FilesByMovie = new Dictionary<int, HashSet<string>>(),
                Utc = DateTime.MinValue
            };

            public List<Movie> Movies { get; set; }
            public Dictionary<int, HashSet<string>> FilesByMovie { get; set; }
            public DateTime Utc { get; set; }
        }
    }
}
