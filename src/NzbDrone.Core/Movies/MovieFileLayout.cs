using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Movies
{
    /// <summary>
    /// Filename matching for libraries where more than one movie shares a directory.
    /// The movie file's stem identifies sidecars. A space after the stem does not match,
    /// so "Heat (1995) Directors Cut" is not treated as an extra of "Heat (1995)".
    /// </summary>
    public static class MovieFileLayout
    {
        private static readonly Regex YearFirstRegex = new Regex(@"^\(?((?:19|20)\d{2})\)?[ .\-_]+(?<title>.+)$", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, ParsedNames> ParsedFileCache = new ConcurrentDictionary<string, ParsedNames>(PathEqualityComparer.Instance);
        private static readonly ConcurrentDictionary<string, string> CleanedTitleCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<Movie, CleanCandidateCache> MovieCleanTitles = new ConditionalWeakTable<Movie, CleanCandidateCache>();
        private static readonly ConditionalWeakTable<ParsedMovieInfo, List<string>> ParsedCleanTitles = new ConditionalWeakTable<ParsedMovieInfo, List<string>>();

        private sealed class ParsedNames
        {
            public ParsedNames(ParsedMovieInfo primary, ParsedMovieInfo yearFirst)
            {
                Primary = primary;
                YearFirst = yearFirst;
            }

            public ParsedMovieInfo Primary { get; }
            public ParsedMovieInfo YearFirst { get; }
        }

        private sealed class CleanCandidateCache
        {
            public object Gate { get; } = new object();
            public string Signature { get; set; }
            public HashSet<string> Titles { get; set; }
        }

        public static bool ExtraFileMatchesMovieStem(string filePath, string movieStem)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);

            if (name.IsNullOrWhiteSpace() || movieStem.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (!name.StartsWith(movieStem, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.Length == movieStem.Length)
            {
                return true;
            }

            var boundary = name[movieStem.Length];

            return boundary is '.' or '-' or '_';
        }

        public static string GetLookupTerm(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parsed = Parser.Parser.ParseMovieTitle(fileName);

            if (parsed?.MovieTitle.IsNotNullOrWhiteSpace() == true)
            {
                return parsed.Year > 0 ? $"{parsed.MovieTitle} ({parsed.Year})" : parsed.MovieTitle;
            }

            if (TryParseYearFirst(fileName, out var title, out var year))
            {
                return $"{title} ({year})";
            }

            return fileName;
        }

        public static bool FileMatchesMovie(Movie movie, string filePath)
        {
            var parsed = ParseCached(filePath);

            if (ParsedTitleMatches(movie, parsed.Primary))
            {
                return true;
            }

            return ParsedTitleMatches(movie, parsed.YearFirst);
        }

        /// <summary>
        /// A flat movie file is the name in the shared directory. Path prefix differences
        /// (a trailing slash, a symlink spelling) must not look like a different file.
        /// </summary>
        public static bool SameLibraryFile(string moviePath, string relativePath, string diskPath)
        {
            if (moviePath.IsNullOrWhiteSpace() || relativePath.IsNullOrWhiteSpace() || diskPath.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (PathsEqual(Path.Combine(moviePath, relativePath), diskPath))
            {
                return true;
            }

            if (relativePath.Contains('/') || relativePath.Contains('\\'))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(diskPath);

            return directory.IsNotNullOrWhiteSpace() &&
                   PathsEqual(directory, moviePath) &&
                   FileNamesEqual(Path.GetFileName(diskPath), relativePath);
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return left.PathEquals(right);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool FileNamesEqual(string left, string right)
        {
            var comparison = OsInfo.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            return string.Equals(left, right, comparison);
        }

        public static bool IsPresent(string moviePath, string relativePath, ISet<string> filesOnDisk)
        {
            if (filesOnDisk == null || filesOnDisk.Count == 0)
            {
                return false;
            }

            if (moviePath.IsNullOrWhiteSpace() || relativePath.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (filesOnDisk.Contains(Path.Combine(moviePath, relativePath)))
            {
                return true;
            }

            // A path-aware set already missed the combined path. Nested relative paths cannot
            // match by file name alone, so the linear fallback would not find them either.
            if (relativePath.IndexOfAny(new[] { '/', '\\' }) >= 0 &&
                filesOnDisk is HashSet<string> pathSet &&
                ReferenceEquals(pathSet.Comparer, PathEqualityComparer.Instance))
            {
                return false;
            }

            return filesOnDisk.Any(file => SameLibraryFile(moviePath, relativePath, file));
        }

        private static ParsedNames ParseCached(string filePath)
        {
            if (filePath.IsNullOrWhiteSpace())
            {
                return new ParsedNames(null, null);
            }

            if (ParsedFileCache.Count > 20000)
            {
                ParsedFileCache.Clear();
            }

            return ParsedFileCache.GetOrAdd(filePath, ParseNames);
        }

        private static ParsedNames ParseNames(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var primary = Parser.Parser.ParseMovieTitle(fileName, true);

            if (primary == null)
            {
                var directory = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);

                if (directory.IsNotNullOrWhiteSpace())
                {
                    primary = Parser.Parser.ParseMovieTitle(directory + " " + fileName) ??
                              Parser.Parser.ParseMovieTitle(directory + Path.GetExtension(fileName));
                }
            }

            ParsedMovieInfo yearFirst = null;
            var stem = Path.GetFileNameWithoutExtension(filePath);

            if (TryParseYearFirst(stem, out var title, out var year))
            {
                yearFirst = new ParsedMovieInfo
                {
                    MovieTitles = new List<string> { title },
                    Year = year
                };
            }

            return new ParsedNames(primary, yearFirst);
        }

        private static bool TryParseYearFirst(string fileName, out string title, out int year)
        {
            title = null;
            year = 0;

            if (fileName.IsNullOrWhiteSpace())
            {
                return false;
            }

            var match = YearFirstRegex.Match(fileName);

            if (!match.Success || !int.TryParse(match.Groups[1].Value, out year))
            {
                return false;
            }

            title = match.Groups["title"].Value.Trim();

            return title.IsNotNullOrWhiteSpace();
        }

        public static bool ParsedTitleMatches(Movie movie, ParsedMovieInfo parsed)
        {
            if (movie == null || parsed == null)
            {
                return false;
            }

            var parsedTitles = CleanParsedTitles(parsed);

            if (parsedTitles.Count == 0)
            {
                return false;
            }

            var cleanCandidates = CleanCandidates(movie);

            if (!parsedTitles.Any(title => cleanCandidates.Contains(title)))
            {
                return false;
            }

            if (parsed.Year > 0 && movie.Year > 0 && parsed.Year != movie.Year)
            {
                var secondaryYear = movie.MovieMetadata?.Value?.SecondaryYear ?? 0;

                if (secondaryYear <= 0 || parsed.Year != secondaryYear)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> CleanParsedTitles(ParsedMovieInfo parsed)
        {
            return ParsedCleanTitles.GetValue(parsed, static info =>
            {
                var titles = new List<string>();

                foreach (var title in info.MovieTitles ?? new List<string>())
                {
                    if (title.IsNotNullOrWhiteSpace())
                    {
                        titles.Add(CleanTitleOnce(title));
                    }
                }

                return titles;
            });
        }

        private static HashSet<string> CleanCandidates(Movie movie)
        {
            var entry = MovieCleanTitles.GetOrCreateValue(movie);

            lock (entry.Gate)
            {
                var collected = CollectTitles(movie);

                if (entry.Titles != null && entry.Signature == collected.Signature)
                {
                    return entry.Titles;
                }

                var cleaned = new HashSet<string>(StringComparer.Ordinal);

                foreach (var title in collected.Values)
                {
                    cleaned.Add(CleanTitleOnce(title));
                }

                entry.Signature = collected.Signature;
                entry.Titles = cleaned;
                return cleaned;
            }
        }

        private static (List<string> Values, string Signature) CollectTitles(Movie movie)
        {
            var values = new List<string>();
            var signature = new StringBuilder();

            void Add(string title)
            {
                if (title.IsNullOrWhiteSpace())
                {
                    return;
                }

                values.Add(title);
                signature.Append(title.Length).Append(':').Append(title).Append('\n');
            }

            Add(movie.Title);

            var metadata = movie.MovieMetadata?.Value;

            if (metadata != null)
            {
                Add(metadata.OriginalTitle);

                if (metadata.AlternativeTitles != null)
                {
                    foreach (var alternative in metadata.AlternativeTitles)
                    {
                        Add(alternative?.Title);
                    }
                }
            }

            return (values, signature.ToString());
        }

        private static string CleanTitleOnce(string title)
        {
            if (CleanedTitleCache.Count > 50000)
            {
                CleanedTitleCache.Clear();
            }

            return CleanedTitleCache.GetOrAdd(title, static value => value.CleanMovieTitle());
        }
    }
}
