using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, MovieFile>
    {
        private Movie _movie1;
        private Movie _movie2;

        [SetUp]
        public void Setup()
        {
            _movie1 = Builder<Movie>.CreateNew()
                                    .With(s => s.Id = 7)
                                    .Build();

            _movie2 = Builder<Movie>.CreateNew()
                                    .With(s => s.Id = 8)
                                    .Build();
        }

        [Test]
        public void get_files_by_movie()
        {
            var files = Builder<MovieFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel())
                .With(c => c.Languages = new List<Language> { Language.English })
                .Random(4)
                .With(s => s.MovieId = 12)
                .BuildListOfNew();

            Db.InsertMany(files);

            var movieFiles = Subject.GetFilesByMovie(12);

            movieFiles.Should().HaveCount(4);
            movieFiles.Should().OnlyContain(c => c.MovieId == 12);
        }

        [Test]
        public void should_delete_files_by_movieId()
        {
            var items = Builder<MovieFile>.CreateListOfSize(5)
                .TheFirst(1)
                .With(c => c.MovieId = _movie2.Id)
                .TheRest()
                .With(c => c.MovieId = _movie1.Id)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .BuildListOfNew();

            Db.InsertMany(items);

            Subject.DeleteForMovies(new List<int> { _movie1.Id });

            var removedItems = Subject.GetFilesByMovie(_movie1.Id);
            var nonRemovedItems = Subject.GetFilesByMovie(_movie2.Id);

            removedItems.Should().HaveCount(0);
            nonRemovedItems.Should().HaveCount(1);
        }

        [Test]
        public void get_relative_paths_by_movies_returns_only_those_movies()
        {
            var files = Builder<MovieFile>.CreateListOfSize(4)
                .All()
                .With(file => file.Id = 0)
                .With(file => file.Quality = new QualityModel())
                .With(file => file.Languages = new List<Language> { Language.English })
                .With(file => file.RelativePath = "movie.mkv")
                .TheFirst(2)
                .With(file => file.MovieId = _movie1.Id)
                .TheRest()
                .With(file => file.MovieId = _movie2.Id)
                .BuildListOfNew();

            Db.InsertMany(files);

            var paths = Subject.GetRelativePathsByMovies(new List<int> { _movie1.Id, _movie1.Id });

            paths.Should().HaveCount(2);
            paths.Should().OnlyContain(path => path.MovieId == _movie1.Id && path.RelativePath == "movie.mkv");
            Subject.GetRelativePathsByMovies(new List<int>()).Should().BeEmpty();
        }
    }
}
