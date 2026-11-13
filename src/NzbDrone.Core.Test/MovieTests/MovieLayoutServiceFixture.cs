using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieLayoutServiceFixture : CoreTest<MovieLayoutService>
    {
        private string _root;
        private Movie _nocturnal;
        private Movie _heat;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Movies".AsOsAgnostic();

            _nocturnal = new Movie
            {
                Id = 1,
                Title = "Nocturnal Animals",
                Year = 2016,
                Path = _root
            };

            _heat = new Movie
            {
                Id = 2,
                Title = "Heat",
                Year = 1995,
                Path = _root
            };

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(It.IsAny<string>()))
                .Returns(MovieFolderLayout.Flat);

            Mocker.GetMock<IMovieRepository>()
                .Setup(repository => repository.AllMoviePaths())
                .Returns(new Dictionary<int, string>
                {
                    { _nocturnal.Id, _root },
                    { _heat.Id, _root }
                });

            Mocker.GetMock<IMovieRepository>()
                .Setup(repository => repository.Get(It.IsAny<IEnumerable<int>>()))
                .Returns((IEnumerable<int> ids) => ids.Select(id => id == _heat.Id ? _heat : _nocturnal).ToList());

            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFilesByMovie(_nocturnal.Id))
                .Returns(new List<MovieFile>());

            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFilesByMovie(_heat.Id))
                .Returns(new List<MovieFile>
                {
                    new MovieFile { MovieId = _heat.Id, RelativePath = "Heat (1995).mkv" }
                });
        }

        [Test]
        public void should_keep_only_the_matching_movie_when_a_folder_contains_several()
        {
            var nocturnalFile = Path.Combine(_root, "Nocturnal Animals (2016).mkv");
            var heatFile = Path.Combine(_root, "Heat (1995).mkv");

            var selected = Subject.SelectVideoFiles(_nocturnal, new List<string> { nocturnalFile, heatFile });

            selected.Should().Equal(nocturnalFile);
        }

        [Test]
        public void should_keep_every_file_for_a_dedicated_movie_folder()
        {
            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetFolderLayout(It.IsAny<string>()))
                .Returns(MovieFolderLayout.MovieFolder);

            var files = new List<string>
            {
                Path.Combine(_root, "one.mkv"),
                Path.Combine(_root, "two.mkv")
            };

            Subject.SelectVideoFiles(_nocturnal, files).Should().Equal(files);
        }

        [Test]
        public void should_delete_only_the_movie_file_and_its_matching_extras()
        {
            var nocturnalFile = Path.Combine(_root, "Nocturnal Animals (2016).mkv");
            var nocturnalNfo = Path.Combine(_root, "Nocturnal Animals (2016).nfo");
            var nocturnalSub = Path.Combine(_root, "Nocturnal Animals (2016).en.srt");
            var heatNfo = Path.Combine(_root, "Heat (1995).nfo");
            var sharedPoster = Path.Combine(_root, "poster.jpg");

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(_root))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(_root, false))
                .Returns(new[] { nocturnalFile, nocturnalNfo, nocturnalSub, heatNfo, sharedPoster });

            var paths = Subject.GetDeletableFilePaths(_nocturnal, new[] { "Nocturnal Animals (2016).mkv" });

            paths.Should().BeEquivalentTo(new[] { nocturnalFile, nocturnalNfo, nocturnalSub });
            paths.Should().NotContain(heatNfo);
            paths.Should().NotContain(sharedPoster);
        }

        [Test]
        public void should_select_extras_that_belong_to_the_movie_file()
        {
            var nocturnalFile = Path.Combine(_root, "Nocturnal Animals (2016).mkv");
            var nocturnalNfo = Path.Combine(_root, "Nocturnal Animals (2016).nfo");
            var heatNfo = Path.Combine(_root, "Heat (1995).nfo");

            var selected = Subject.SelectExtraFiles(_nocturnal, new List<string> { nocturnalNfo, heatNfo }, new[] { nocturnalFile });

            selected.Should().Equal(nocturnalNfo);
        }
    }
}
