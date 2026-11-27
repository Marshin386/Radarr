using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieFileLayoutFixture : CoreTest
    {
        [TestCase("Heat (1995).nfo", "Heat (1995)", true)]
        [TestCase("Heat (1995).en.srt", "Heat (1995)", true)]
        [TestCase("Heat (1995)-poster.jpg", "Heat (1995)", true)]
        [TestCase("Heat (1995)_trailer.mkv", "Heat (1995)", true)]
        [TestCase("Nocturnal Animals (2016).nfo", "Heat (1995)", false)]
        [TestCase("Heat (1995) Directors Cut.nfo", "Heat (1995)", false)]
        [TestCase("poster.jpg", "Heat (1995)", false)]
        public void should_match_only_sidecars_for_the_movie_stem(string fileName, string stem, bool expected)
        {
            MovieFileLayout.ExtraFileMatchesMovieStem(fileName, stem).Should().Be(expected);
        }

        [Test]
        public void should_match_parsed_file_to_the_movie_and_reject_a_different_movie()
        {
            var movie = new Movie
            {
                Title = "Nocturnal Animals",
                Year = 2016
            };

            var match = Parser.Parser.ParseMoviePath(Path.Combine("/movies", "Nocturnal Animals (2016).mkv"));
            var other = Parser.Parser.ParseMoviePath(Path.Combine("/movies", "Heat (1995).mkv"));

            match.Should().NotBeNull();
            other.Should().NotBeNull();
            MovieFileLayout.ParsedTitleMatches(movie, match).Should().BeTrue();
            MovieFileLayout.ParsedTitleMatches(movie, other).Should().BeFalse();
        }

        [Test]
        public void should_build_a_lookup_term_from_a_nested_file_name()
        {
            MovieFileLayout.GetLookupTerm("/Theatre/Movie Series/Marvel Movies/(2008) Iron Man.mkv")
                .Should().Be("Iron Man (2008)");
        }

        [Test]
        public void should_match_a_year_first_file_name_to_the_movie()
        {
            var movie = new Movie
            {
                Title = "Iron Man",
                Year = 2008
            };

            MovieFileLayout.FileMatchesMovie(movie, "/Theatre/Movie Series/Marvel Movies/(2008) Iron Man.mkv")
                .Should().BeTrue();

            MovieFileLayout.FileMatchesMovie(movie, "/Theatre/Movie Series/Marvel Movies/(2010) Iron Man 2.mkv")
                .Should().BeFalse();
        }

        [Test]
        public void should_treat_the_same_flat_file_name_as_one_file()
        {
            var folder = @"C:\library\movies".AsOsAgnostic();
            var onDisk = Path.Combine(folder, "Example (2020).mp4");

            MovieFileLayout.SameLibraryFile(folder, "Example (2020).mp4", onDisk).Should().BeTrue();
            MovieFileLayout.SameLibraryFile(folder, Path.Combine("other", "Example (2020).mp4"), onDisk).Should().BeFalse();
            MovieFileLayout.SameLibraryFile(folder, "Other (2020).mp4", onDisk).Should().BeFalse();
        }

        [Test]
        public void should_match_an_alternative_title_and_still_reject_a_different_year()
        {
            var movie = new Movie
            {
                Title = "Heat",
                Year = 1995
            };

            movie.MovieMetadata.Value.AlternativeTitles = new List<AlternativeTitle>
            {
                new AlternativeTitle("L.A. Takedown")
            };

            var parsed = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { "L.A. Takedown" },
                Year = 1995
            };

            MovieFileLayout.ParsedTitleMatches(movie, parsed).Should().BeTrue();
            MovieFileLayout.ParsedTitleMatches(movie, parsed).Should().BeTrue();

            parsed.Year = 1994;
            MovieFileLayout.ParsedTitleMatches(movie, parsed).Should().BeFalse();
        }

        [Test]
        public void should_find_a_flat_file_from_the_path_set_without_a_nested_false_match()
        {
            var folder = @"C:\library\movies".AsOsAgnostic();
            var onDisk = Path.Combine(folder, "Example (2020).mp4");
            var files = new HashSet<string>(PathEqualityComparer.Instance) { onDisk };

            MovieFileLayout.IsPresent(folder, "Example (2020).mp4", files).Should().BeTrue();
            MovieFileLayout.IsPresent(folder, Path.Combine("other", "Example (2020).mp4"), files).Should().BeFalse();
            MovieFileLayout.IsPresent(folder, "Missing (2020).mp4", files).Should().BeFalse();
        }
    }
}
