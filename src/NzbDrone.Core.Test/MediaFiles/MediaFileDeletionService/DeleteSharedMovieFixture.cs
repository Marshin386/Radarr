using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class DeleteSharedMovieFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private string _root;
        private Movie _movie;
        private string _movieFile;
        private string _extraFile;
        private string _otherMovieFile;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Movies".AsOsAgnostic();
            _movieFile = Path.Combine(_root, "Nocturnal Animals (2016).mkv");
            _extraFile = Path.Combine(_root, "Nocturnal Animals (2016).nfo");
            _otherMovieFile = Path.Combine(_root, "Heat (1995).mkv");

            _movie = new Movie
            {
                Id = 11,
                Title = "Nocturnal Animals",
                Year = 2016,
                Path = _root
            };

            Mocker.GetMock<IMovieService>()
                .Setup(service => service.AllMoviePaths())
                .Returns(new Dictionary<int, string>
                {
                    { _movie.Id, _root },
                    { 12, _root }
                });

            Mocker.GetMock<IMovieLayoutService>()
                .Setup(service => service.GetLayout(_movie))
                .Returns(MovieFolderLayout.Flat);

            Mocker.GetMock<IMovieLayoutService>()
                .Setup(service => service.GetDeletableFilePaths(_movie, It.IsAny<IEnumerable<string>>()))
                .Returns(new[] { _movieFile, _extraFile });

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetParentFolder(_movieFile))
                .Returns(_root);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetParentFolder(_extraFile))
                .Returns(_root);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetParentFolder(_root))
                .Returns(Path.GetDirectoryName(_root));
        }

        [Test]
        public void should_delete_only_that_movies_files_from_a_flat_folder()
        {
            var deleted = new MoviesDeletedEvent(new List<Movie> { _movie }, true, false)
            {
                OwnedRelativePaths = new Dictionary<int, List<string>>
                {
                    { _movie.Id, new List<string> { "Nocturnal Animals (2016).mkv" } }
                }
            };

            Subject.HandleAsync(deleted);

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(bin => bin.DeleteFolder(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(bin => bin.DeleteFile(_movieFile, It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(bin => bin.DeleteFile(_extraFile, It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(bin => bin.DeleteFile(_otherMovieFile, It.IsAny<string>()), Times.Never());
        }
    }
}
