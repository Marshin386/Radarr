using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
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

    public class MovieLayoutService : IMovieLayoutService
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly IMovieRepository _movieRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IKeptMovieFileRepository _keptMovieFileRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

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

            foreach (var other in OtherMoviesSharingPath(movie))
            {
                foreach (var file in _mediaFileService.GetFilesByMovie(other.Id) ?? new List<MovieFile>())
                {
                    if (file.RelativePath.IsNotNullOrWhiteSpace())
                    {
                        paths.Add(Path.Combine(other.Path, file.RelativePath));
                    }
                }
            }

            return paths;
        }

        private List<Movie> OtherMoviesSharingPath(Movie movie)
        {
            if (movie?.Path.IsNullOrWhiteSpace() != false)
            {
                return new List<Movie>();
            }

            var ids = (_movieRepository.AllMoviePaths() ?? new Dictionary<int, string>())
                .Where(entry => entry.Key != movie.Id && entry.Value.IsNotNullOrWhiteSpace() && entry.Value.PathEquals(movie.Path))
                .Select(entry => entry.Key)
                .ToList();

            if (!ids.Any())
            {
                return new List<Movie>();
            }

            return _movieRepository.Get(ids).ToList();
        }
    }
}
