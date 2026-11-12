using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    public class MoviePathValidatorFixture : CoreTest<MoviePathValidator>
    {
        private TestValidator<Movie> _validator;
        private string _root;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Movies".AsOsAgnostic();
            _validator = new TestValidator<Movie>
            {
                v => v.RuleFor(s => s.Path).SetValidator(Subject)
            };
        }

        private void GivenLayout(MovieFolderLayout layout)
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(_root))
                .Returns(layout);
        }

        private void GivenOtherMovie()
        {
            Mocker.GetMock<IMovieService>()
                .Setup(service => service.AllMoviePaths())
                .Returns(new Dictionary<int, string> { { 8, _root } });
        }

        [Test]
        public void should_reject_a_second_movie_in_a_movie_folder()
        {
            GivenLayout(MovieFolderLayout.MovieFolder);
            GivenOtherMovie();

            var movie = Builder<Movie>.CreateNew().With(m => m.Id = 2).With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeFalse();
        }

        [Test]
        public void should_allow_several_movies_to_share_a_flat_root()
        {
            GivenLayout(MovieFolderLayout.Flat);
            GivenOtherMovie();

            var movie = Builder<Movie>.CreateNew().With(m => m.Id = 2).With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_allow_several_movies_to_share_a_custom_folder()
        {
            GivenLayout(MovieFolderLayout.Custom);
            GivenOtherMovie();

            var movie = Builder<Movie>.CreateNew().With(m => m.Id = 2).With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }
    }
}
