using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            if (ParsedTitleMatches(movie, Parser.Parser.ParseMoviePath(filePath)))
            {
                return true;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);

            if (!TryParseYearFirst(fileName, out var title, out var year))
            {
                return false;
            }

            return ParsedTitleMatches(movie, new ParsedMovieInfo
            {
                MovieTitles = new List<string> { title },
                Year = year
            });
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

            var parsedTitles = (parsed.MovieTitles ?? new List<string>())
                .Where(title => title.IsNotNullOrWhiteSpace())
                .Select(title => title.CleanMovieTitle())
                .ToList();

            if (!parsedTitles.Any())
            {
                return false;
            }

            var candidates = new List<string>();

            if (movie.Title.IsNotNullOrWhiteSpace())
            {
                candidates.Add(movie.Title);
            }

            var metadata = movie.MovieMetadata?.Value;

            if (metadata != null)
            {
                if (metadata.OriginalTitle.IsNotNullOrWhiteSpace())
                {
                    candidates.Add(metadata.OriginalTitle);
                }

                if (metadata.AlternativeTitles != null)
                {
                    candidates.AddRange(metadata.AlternativeTitles.Where(title => title != null).Select(title => title.Title));
                }
            }

            var cleanCandidates = candidates
                .Where(title => title.IsNotNullOrWhiteSpace())
                .Select(title => title.CleanMovieTitle())
                .ToList();

            if (!parsedTitles.Any(title => cleanCandidates.Contains(title)))
            {
                return false;
            }

            if (parsed.Year > 0 && movie.Year > 0 && parsed.Year != movie.Year)
            {
                var secondaryYear = metadata?.SecondaryYear ?? 0;

                if (secondaryYear <= 0 || parsed.Year != secondaryYear)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
