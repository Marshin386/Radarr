using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.RootFolders
{
    public interface IRootFolderService
    {
        List<RootFolder> All();
        List<RootFolder> AllWithUnmappedFolders();
        RootFolder Add(RootFolder rootDir);
        RootFolder Update(RootFolder rootFolder);
        void Remove(int id);
        RootFolder Get(int id, bool timeout);
        string GetBestRootFolderPath(string path, List<RootFolder> rootFolders = null);
        MovieFolderLayout GetFolderLayout(string path);
        void ApplyLibraryLayout(MovieFolderLayout layout);
    }

    public class RootFolderService : IRootFolderService
    {
        private static readonly TimeSpan UnmappedCacheLifetime = TimeSpan.FromSeconds(15);

        private readonly IRootFolderRepository _rootFolderRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly IMovieRepository _movieRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly IKeptMovieFileRepository _keptMovieFileRepository;
        private readonly IConfigService _configService;
        private readonly INamingConfigService _namingConfigService;
        private readonly Logger _logger;

        private readonly ICached<string> _cache;
        private readonly object _scanGate = new object();
        private readonly Dictionary<string, InFlightScan> _scans = new Dictionary<string, InFlightScan>(PathEqualityComparer.Instance);

        private static readonly HashSet<string> SpecialFolders = new HashSet<string>
                                                                 {
                                                                     "$recycle.bin",
                                                                     "system volume information",
                                                                     "recycler",
                                                                     "lost+found",
                                                                     ".appledb",
                                                                     ".appledesktop",
                                                                     ".appledouble",
                                                                     "@eadir",
                                                                     ".grab"
                                                                 };

        public RootFolderService(IRootFolderRepository rootFolderRepository,
                                 IDiskProvider diskProvider,
                                 IMovieRepository movieRepository,
                                 IMediaFileService mediaFileService,
                                 IKeptMovieFileRepository keptMovieFileRepository,
                                 IConfigService configService,
                                 INamingConfigService namingConfigService,
                                 ICacheManager cacheManager,
                                 Logger logger)
        {
            _rootFolderRepository = rootFolderRepository;
            _diskProvider = diskProvider;
            _movieRepository = movieRepository;
            _mediaFileService = mediaFileService;
            _keptMovieFileRepository = keptMovieFileRepository;
            _configService = configService;
            _namingConfigService = namingConfigService;
            _logger = logger;

            _cache = cacheManager.GetCache<string>(GetType());
        }

        public List<RootFolder> All()
        {
            var rootFolders = _rootFolderRepository.All().ToList();

            return rootFolders;
        }

        public List<RootFolder> AllWithUnmappedFolders()
        {
            var rootFolders = _rootFolderRepository.All().ToList();

            var moviePaths = _movieRepository.AllMoviePaths();

            rootFolders.ForEach(folder =>
            {
                try
                {
                    if (folder.Path.IsPathValid(PathValidationType.CurrentOs))
                    {
                        GetDetails(folder, moviePaths, true);
                    }
                }

                // We don't want an exception to prevent the root folders from loading in the UI, so they can still be deleted
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to get free space and unmapped folders for root folder {0}", folder.Path);
                    folder.UnmappedFolders = new List<UnmappedFolder>();
                }
            });

            return rootFolders;
        }

        public RootFolder Add(RootFolder rootFolder)
        {
            var all = All();

            if (string.IsNullOrWhiteSpace(rootFolder.Path) || !Path.IsPathRooted(rootFolder.Path))
            {
                throw new ArgumentException("Invalid path");
            }

            if (!_diskProvider.FolderExists(rootFolder.Path))
            {
                throw new DirectoryNotFoundException("Can't add root directory that doesn't exist.");
            }

            if (all.Exists(r => r.Path.PathEquals(rootFolder.Path)))
            {
                throw new InvalidOperationException("Recent directory already exists.");
            }

            if (!_diskProvider.FolderWritable(rootFolder.Path))
            {
                throw new UnauthorizedAccessException($"Root folder path '{rootFolder.Path}' is not writable by user '{Environment.UserName}'");
            }

            rootFolder.FolderLayout = _namingConfigService.GetConfig().FolderLayout;

            _rootFolderRepository.Insert(rootFolder);

            var moviePaths = _movieRepository.AllMoviePaths();

            GetDetails(rootFolder, moviePaths, true);
            ClearCaches();

            return rootFolder;
        }

        public RootFolder Update(RootFolder rootFolder)
        {
            var existing = _rootFolderRepository.Get(rootFolder.Id);

            existing.FolderLayout = rootFolder.FolderLayout;
            _rootFolderRepository.SetFields(existing, folder => folder.FolderLayout);
            ClearCaches();

            return existing;
        }

        public void ApplyLibraryLayout(MovieFolderLayout layout)
        {
            foreach (var root in _rootFolderRepository.All().ToList())
            {
                if (root.FolderLayout == layout)
                {
                    continue;
                }

                root.FolderLayout = layout;
                _rootFolderRepository.SetFields(root, folder => folder.FolderLayout);
            }

            ClearCaches();
        }

        public void Remove(int id)
        {
            _rootFolderRepository.Delete(id);
            ClearCaches();
        }

        private List<UnmappedFolder> GetUnmappedFolders(string path, Dictionary<int, string> moviePaths, CancellationToken cancellationToken)
        {
            _logger.Debug("Generating list of unmapped folders");
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Invalid path provided", nameof(path));
            }

            var results = new List<UnmappedFolder>();

            if (!_diskProvider.FolderExists(path))
            {
                _logger.Debug("Path supplied does not exist: {0}", path);
                return results;
            }

            var context = CreateLayoutContext(path, moviePaths);
            var keptPaths = KeptFilePaths();
            var keptDirectories = new HashSet<string>(
                keptPaths.Select(filePath => Path.GetDirectoryName(filePath)).Where(directory => directory.IsNotNullOrWhiteSpace()),
                PathEqualityComparer.Instance);

            if (context.GetLayout(path) != MovieFolderLayout.MovieFolder)
            {
                return GetUnmappedMovieFiles(path, moviePaths, keptPaths, context, cancellationToken);
            }

            var subFolderDepth = context.NamingConfig.MovieFolderFormat.Count(f => f == Path.DirectorySeparatorChar);
            var possibleMovieFolders = _diskProvider.GetDirectories(path).ToList();

            if (subFolderDepth > 0)
            {
                for (var i = 0; i < subFolderDepth; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    possibleMovieFolders = possibleMovieFolders.SelectMany(_diskProvider.GetDirectories).ToList();
                }
            }

            var unmappedFolders = possibleMovieFolders.Except(moviePaths.Select(s => s.Value), PathEqualityComparer.Instance).ToList();

            var recycleBinPath = _configService.RecycleBin;

            foreach (var unmappedFolder in unmappedFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var di = new DirectoryInfo(unmappedFolder.Normalize());

                if (keptDirectories.Contains(di.FullName))
                {
                    continue;
                }

                if ((!di.Attributes.HasFlag(FileAttributes.System) && !di.Attributes.HasFlag(FileAttributes.Hidden)) || di.Attributes.ToString() == "-1")
                {
                    if (string.IsNullOrWhiteSpace(recycleBinPath) || di.FullName.PathNotEquals(recycleBinPath))
                    {
                        results.Add(new UnmappedFolder
                        {
                            Name = di.Name,
                            Path = di.FullName,
                            RelativePath = path.GetRelativePath(di.FullName)
                        });
                    }
                }
            }

            var setToRemove = SpecialFolders;
            results.RemoveAll(x => setToRemove.Contains(new DirectoryInfo(x.Path.ToLowerInvariant()).Name));

            _logger.Debug("{0} unmapped folders detected.", results.Count);
            return results.OrderBy(u => u.Name, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        public RootFolder Get(int id, bool timeout)
        {
            var rootFolder = _rootFolderRepository.Get(id);
            var moviePaths = _movieRepository.AllMoviePaths();

            GetDetails(rootFolder, moviePaths, timeout);

            return rootFolder;
        }

        public string GetBestRootFolderPath(string path, List<RootFolder> rootFolders = null)
        {
            return _cache.Get(path, () => GetBestRootFolderPathInternal(path, rootFolders), TimeSpan.FromDays(1));
        }

        public MovieFolderLayout GetFolderLayout(string path)
        {
            if (path.IsNullOrWhiteSpace() || !path.IsPathValid(PathValidationType.CurrentOs))
            {
                return MovieFolderLayout.MovieFolder;
            }

            var match = FindMatchingRoot(path, All());

            if (match == null)
            {
                return MovieFolderLayout.MovieFolder;
            }

            // One library structure for every root. The column is kept in sync when the setting is saved.
            return _namingConfigService.GetConfig().FolderLayout;
        }

        private void GetDetails(RootFolder rootFolder, Dictionary<int, string> moviePaths, bool timeout)
        {
            var wait = timeout ? TimeSpan.FromSeconds(5) : Timeout.InfiniteTimeSpan;
            GetDetails(rootFolder, moviePaths, wait);
        }

        internal void GetDetails(RootFolder rootFolder, Dictionary<int, string> moviePaths, TimeSpan wait)
        {
            if (rootFolder == null || rootFolder.Path.IsNullOrWhiteSpace())
            {
                return;
            }

            var path = rootFolder.Path;
            InFlightScan scan;
            var useCache = false;

            lock (_scanGate)
            {
                _scans.TryGetValue(path, out var existing);

                if (existing != null && IsFresh(existing))
                {
                    scan = existing;
                    useCache = true;
                }
                else if (existing?.Task != null && !existing.Task.IsCompleted && existing.Cancellation != null && !existing.Cancellation.IsCancellationRequested)
                {
                    existing.Waiters++;
                    scan = existing;
                }
                else
                {
                    if (existing?.Task != null && !existing.Task.IsCompleted)
                    {
                        existing.Cancellation?.Cancel();
                    }

                    scan = new InFlightScan
                    {
                        Cancellation = new CancellationTokenSource(),
                        Waiters = 1
                    };

                    var token = scan.Cancellation.Token;
                    var paths = moviePaths;
                    scan.Task = Task.Run(() => ExecuteScan(path, paths, scan, token));
                    _scans[path] = scan;
                }
            }

            if (useCache)
            {
                ApplyCached(rootFolder, scan);
                return;
            }

            var waitMilliseconds = ToWaitMilliseconds(wait);
            var completed = false;
            var shouldCancel = false;

            try
            {
                completed = scan.Task.Wait(waitMilliseconds);
            }
            catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
            {
                completed = scan.Task.IsCompleted;
            }
            finally
            {
                lock (_scanGate)
                {
                    scan.Waiters--;

                    if (scan.Succeeded)
                    {
                        ApplyScanResult(rootFolder, scan);
                    }
                    else if (!completed && scan.Waiters <= 0)
                    {
                        shouldCancel = true;
                    }
                }
            }

            if (shouldCancel)
            {
                scan.Cancellation.Cancel();
            }
        }

        internal bool IsUnmappedScanCancellationRequested(string path)
        {
            lock (_scanGate)
            {
                return path.IsNotNullOrWhiteSpace() &&
                       _scans.TryGetValue(path, out var scan) &&
                       scan.Cancellation != null &&
                       scan.Cancellation.IsCancellationRequested;
            }
        }

        internal bool WaitForUnmappedScan(string path, TimeSpan timeout)
        {
            Task task;

            lock (_scanGate)
            {
                if (path.IsNullOrWhiteSpace() || !_scans.TryGetValue(path, out var scan) || scan.Task == null)
                {
                    return true;
                }

                task = scan.Task;
            }

            try
            {
                return task.Wait(ToWaitMilliseconds(timeout));
            }
            catch (AggregateException)
            {
                return true;
            }
        }

        private void ExecuteScan(string path, Dictionary<int, string> moviePaths, InFlightScan scan, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (!_diskProvider.FolderExists(path))
                {
                    return;
                }

                var freeSpace = _diskProvider.GetAvailableSpace(path);
                var totalSpace = _diskProvider.GetTotalSize(path);
                var unmapped = GetUnmappedFolders(path, moviePaths, token);

                token.ThrowIfCancellationRequested();

                lock (_scanGate)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    scan.Accessible = true;
                    scan.FreeSpace = freeSpace;
                    scan.TotalSpace = totalSpace;
                    scan.UnmappedFolders = unmapped;
                    scan.Succeeded = true;
                    scan.CompletedUtc = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Cancelled unmapped folder scan for {0}", path);
            }
        }

        private void ApplyCached(RootFolder rootFolder, InFlightScan scan)
        {
            if (!_diskProvider.FolderExists(rootFolder.Path))
            {
                return;
            }

            rootFolder.Accessible = true;
            rootFolder.FreeSpace = _diskProvider.GetAvailableSpace(rootFolder.Path);
            rootFolder.TotalSpace = _diskProvider.GetTotalSize(rootFolder.Path);
            rootFolder.UnmappedFolders = scan.UnmappedFolders?.ToList();
        }

        private static void ApplyScanResult(RootFolder rootFolder, InFlightScan scan)
        {
            rootFolder.Accessible = scan.Accessible;
            rootFolder.FreeSpace = scan.FreeSpace;
            rootFolder.TotalSpace = scan.TotalSpace;
            rootFolder.UnmappedFolders = scan.UnmappedFolders?.ToList();
        }

        private static bool IsFresh(InFlightScan scan)
        {
            return scan.Succeeded && DateTime.UtcNow - scan.CompletedUtc < UnmappedCacheLifetime;
        }

        private static int ToWaitMilliseconds(TimeSpan wait)
        {
            if (wait == Timeout.InfiniteTimeSpan || wait < TimeSpan.Zero)
            {
                return Timeout.Infinite;
            }

            return (int)Math.Min(wait.TotalMilliseconds, int.MaxValue);
        }

        private void ClearCaches()
        {
            _cache.Clear();
            ClearUnmappedScans();
        }

        private void ClearUnmappedScans()
        {
            lock (_scanGate)
            {
                foreach (var scan in _scans.Values)
                {
                    scan.Cancellation?.Cancel();
                }

                _scans.Clear();
            }
        }

        private string GetBestRootFolderPathInternal(string path, List<RootFolder> rootFolders = null)
        {
            var allRootFoldersToConsider = rootFolders ?? All();

            var exactRootFolder = allRootFoldersToConsider.FirstOrDefault(root => root.Path.IsNotNullOrWhiteSpace() && root.Path.PathEquals(path));

            if (exactRootFolder != null)
            {
                return exactRootFolder.Path.GetCleanPath();
            }

            var possibleRootFolder = allRootFoldersToConsider.Where(r => r.Path.IsParentPath(path)).MaxBy(r => r.Path.Length);

            if (possibleRootFolder == null)
            {
                var osPath = new OsPath(path);

                return osPath.Directory.ToString().GetCleanPath();
            }

            return possibleRootFolder.Path.GetCleanPath();
        }

        private HashSet<string> KeptFilePaths()
        {
            return new HashSet<string>(
                (_keptMovieFileRepository.All() ?? Enumerable.Empty<KeptMovieFile>())
                    .Select(file => file.Path)
                    .Where(filePath => filePath.IsNotNullOrWhiteSpace()),
                PathEqualityComparer.Instance);
        }

        private FolderLayoutContext CreateLayoutContext(string path, Dictionary<int, string> moviePaths)
        {
            var context = new FolderLayoutContext(All(), _namingConfigService.GetConfig());

            // Dedicated-folder discovery only runs while listing loose files. A movie-folder
            // library lists directories instead, so there is nothing to classify.
            if (context.GetLayout(path) == MovieFolderLayout.MovieFolder || moviePaths == null)
            {
                return context;
            }

            foreach (var moviePath in moviePaths.Values)
            {
                if (moviePath.IsNullOrWhiteSpace() || !moviePath.IsPathValid(PathValidationType.CurrentOs))
                {
                    continue;
                }

                if (context.GetLayout(moviePath) == MovieFolderLayout.MovieFolder)
                {
                    context.DedicatedMovieFolders.Add(moviePath);
                }
            }

            return context;
        }

        private List<UnmappedFolder> GetUnmappedMovieFiles(string path, Dictionary<int, string> moviePaths, HashSet<string> keptPaths, FolderLayoutContext context, CancellationToken cancellationToken)
        {
            _logger.Debug("Generating list of unmapped movie files for {0}", path);
            cancellationToken.ThrowIfCancellationRequested();

            var results = new List<UnmappedFolder>();

            if (!_diskProvider.FolderExists(path))
            {
                return results;
            }

            var layout = context.GetLayout(path);

            // A flat root has no dedicated movie folders, so a movie path that merely contains
            // the root does not hide files. Other layouts still match those paths by parent directory.
            var checkDedicatedFolders = layout != MovieFolderLayout.Flat && context.DedicatedMovieFolders.Count > 0;
            var files = _diskProvider.GetFiles(path, layout == MovieFolderLayout.Custom) ?? Enumerable.Empty<string>();
            var mappedSharedFiles = MappedSharedMovieFiles(moviePaths, context);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!MediaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                try
                {
                    if (MovieLibraryPathFilter.ShouldIgnore(path, file))
                    {
                        continue;
                    }
                }
                catch (NotParentException)
                {
                    continue;
                }

                if (mappedSharedFiles.Contains(file) || keptPaths.Contains(file) || (checkDedicatedFolders && IsInsideDedicatedMovieFolder(file, context)))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(file);

                if (directory.IsNullOrWhiteSpace())
                {
                    continue;
                }

                results.Add(new UnmappedFolder
                {
                    Name = MovieFileLayout.GetLookupTerm(file),
                    Path = directory,
                    RelativePath = path.GetRelativePath(file)
                });
            }

            _logger.Debug("{0} unmapped movie files detected.", results.Count);
            return results.OrderBy(item => item.RelativePath, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        private HashSet<string> MappedSharedMovieFiles(Dictionary<int, string> moviePaths, FolderLayoutContext context)
        {
            var mapped = new HashSet<string>(PathEqualityComparer.Instance);
            var sharedMovieIds = (moviePaths ?? new Dictionary<int, string>())
                .Where(entry => entry.Value.IsNotNullOrWhiteSpace() && context.GetLayout(entry.Value) != MovieFolderLayout.MovieFolder)
                .Select(entry => entry.Key)
                .ToList();

            if (sharedMovieIds.Count == 0)
            {
                return mapped;
            }

            foreach (var movieFile in _mediaFileService.GetRelativePathsByMovies(sharedMovieIds) ?? new List<MovieFilePath>())
            {
                if (movieFile == null || movieFile.RelativePath.IsNullOrWhiteSpace() || !moviePaths.TryGetValue(movieFile.MovieId, out var moviePath))
                {
                    continue;
                }

                mapped.Add(Path.Combine(moviePath, movieFile.RelativePath));
            }

            return mapped;
        }

        private static bool IsInsideDedicatedMovieFolder(string file, FolderLayoutContext context)
        {
            if (context.DedicatedMovieFolders.Count == 0)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(file);

            while (directory.IsNotNullOrWhiteSpace())
            {
                if (context.DedicatedMovieFolders.Contains(directory))
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(directory);

                if (parent.IsNullOrWhiteSpace() || parent.PathEquals(directory))
                {
                    break;
                }

                directory = parent;
            }

            return false;
        }

        private static RootFolder FindMatchingRoot(string path, IEnumerable<RootFolder> roots)
        {
            if (path.IsNullOrWhiteSpace() || !path.IsPathValid(PathValidationType.CurrentOs) || roots == null)
            {
                return null;
            }

            return roots
                .Where(root => root.Path.IsNotNullOrWhiteSpace() && root.Path.IsPathValid(PathValidationType.CurrentOs))
                .Where(root => root.Path.PathEquals(path) || root.Path.IsParentPath(path))
                .MaxBy(root => root.Path.Length);
        }

        private sealed class InFlightScan
        {
            public Task Task { get; set; }
            public CancellationTokenSource Cancellation { get; set; }
            public int Waiters { get; set; }
            public bool Succeeded { get; set; }
            public bool Accessible { get; set; }
            public long? FreeSpace { get; set; }
            public long? TotalSpace { get; set; }
            public List<UnmappedFolder> UnmappedFolders { get; set; }
            public DateTime CompletedUtc { get; set; }
        }

        private sealed class FolderLayoutContext
        {
            private readonly List<RootFolder> _roots;
            private readonly MovieFolderLayout _libraryLayout;
            private readonly Dictionary<string, MovieFolderLayout> _layouts = new Dictionary<string, MovieFolderLayout>(PathEqualityComparer.Instance);

            public FolderLayoutContext(IEnumerable<RootFolder> roots, NamingConfig namingConfig)
            {
                _roots = roots?.ToList() ?? new List<RootFolder>();
                NamingConfig = namingConfig ?? NamingConfig.Default;
                _libraryLayout = NamingConfig.FolderLayout;
                DedicatedMovieFolders = new HashSet<string>(PathEqualityComparer.Instance);
            }

            public NamingConfig NamingConfig { get; }

            public HashSet<string> DedicatedMovieFolders { get; }

            public MovieFolderLayout GetLayout(string path)
            {
                if (path.IsNullOrWhiteSpace())
                {
                    return MovieFolderLayout.MovieFolder;
                }

                if (_layouts.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                var match = FindMatchingRoot(path, _roots);
                var layout = match == null ? MovieFolderLayout.MovieFolder : _libraryLayout;
                _layouts[path] = layout;
                return layout;
            }
        }
    }
}
