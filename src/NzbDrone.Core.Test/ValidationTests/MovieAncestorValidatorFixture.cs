using System.Collections.Generic;
using System.IO;
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
    public class MovieAncestorValidatorFixture : CoreTest<MovieAncestorValidator>
    {
        private TestValidator<Movie> _validator;
        private string _root;
        private string _child;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Movies".AsOsAgnostic();
            _child = Path.Combine(_root, "Heat (1995)");
            _validator = new TestValidator<Movie>
            {
                v => v.RuleFor(s => s.Path).SetValidator(Subject)
            };

            Mocker.GetMock<IMovieService>()
                .Setup(service => service.AllMoviePaths())
                .Returns(new Dictionary<int, string> { { 8, _child } });
        }

        [Test]
        public void should_reject_a_movie_folder_that_contains_another_movie()
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(_root))
                .Returns(MovieFolderLayout.MovieFolder);

            var movie = Builder<Movie>.CreateNew().With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeFalse();
        }

        [Test]
        public void should_allow_a_flat_root_that_contains_another_movie_folder()
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(_root))
                .Returns(MovieFolderLayout.Flat);

            var movie = Builder<Movie>.CreateNew().With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }
    }
}
