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
    public class RootFolderValidatorFixture : CoreTest<RootFolderValidator>
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

        private void GivenRoot(MovieFolderLayout layout)
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.All())
                .Returns(new List<RootFolder>
                {
                    new RootFolder { Id = 1, Path = _root, FolderLayout = layout }
                });

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(_root))
                .Returns(layout);
        }

        [Test]
        public void should_reject_a_movie_folder_root_as_the_movie_path()
        {
            GivenRoot(MovieFolderLayout.MovieFolder);

            var movie = Builder<Movie>.CreateNew().With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeFalse();
        }

        [Test]
        public void should_allow_a_flat_root_as_the_movie_path()
        {
            GivenRoot(MovieFolderLayout.Flat);

            var movie = Builder<Movie>.CreateNew().With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_custom_root_as_the_movie_path()
        {
            GivenRoot(MovieFolderLayout.Custom);

            var movie = Builder<Movie>.CreateNew().With(m => m.Path = _root).Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_allow_a_child_of_a_movie_folder_root()
        {
            GivenRoot(MovieFolderLayout.MovieFolder);

            var movie = Builder<Movie>.CreateNew()
                .With(m => m.Path = System.IO.Path.Combine(_root, "Heat (1995)"))
                .Build();

            _validator.Validate(movie).IsValid.Should().BeTrue();
        }
    }
}
