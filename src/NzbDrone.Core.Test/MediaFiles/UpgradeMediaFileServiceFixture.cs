using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _localMovie = new LocalMovie();
            _localMovie.Movie = new Movie
            {
                Path = @"C:\Test\Movies\Movie".AsOsAgnostic()
            };

            _movieFile = Builder<MovieFile>
                  .CreateNew()
                  .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localMovie.Movie.Path).FullName))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                  .Returns<string>(c => Path.GetDirectoryName(c));
        }

        private void GivenSingleMovieWithSingleMovieFile()
        {
            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Id = 1,
                    RelativePath = @"A.Movie.2019.avi",
                };
        }

        [Test]
        public void should_delete_single_movie_file_once()
        {
            GivenSingleMovieWithSingleMovieFile();

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_movie_file_from_database()
        {
            GivenSingleMovieWithSingleMovieFile();

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_old_movie_file_in_oldFiles()
        {
            GivenSingleMovieWithSingleMovieFile();

            Subject.UpgradeMovieFile(_movieFile, _localMovie).OldFiles.Count.Should().Be(1);
        }

        [Test]
        public void should_throw_if_there_are_existing_movie_files_and_the_root_folder_is_missing()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localMovie.Movie.Path).FullName))
                  .Returns(false);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeMovieFile(_movieFile, _localMovie));

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_move_an_upgrade_into_a_designated_flat_folder_and_update_the_movie_path()
        {
            GivenSingleMovieWithSingleMovieFile();

            var destination = @"C:\Upgrades".AsOsAgnostic();
            var oldPath = _localMovie.Movie.Path;
            _localMovie.Movie.QualityProfile = new QualityProfile
            {
                UpgradeAllowed = true,
                UpgradeFilePlacement = UpgradeFilePlacement.MoveToDesignatedLocation,
                UpgradeDestination = destination
            };

            Mocker.GetMock<IBuildMoviePaths>()
                .Setup(builder => builder.BuildLibraryPath(_localMovie.Movie, destination))
                .Returns(destination);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(destination))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(It.IsAny<string>(), false))
                .Returns(new List<string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetDirectories(It.IsAny<string>()))
                .Returns(new List<string>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetBestRootFolderPath(oldPath, null))
                .Returns(Directory.GetParent(oldPath).FullName);

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            _localMovie.Movie.Path.Should().Be(destination);
            Mocker.GetMock<IMovieService>()
                .Verify(service => service.UpdateMovie(It.Is<Movie>(movie => movie.Path == destination)), Times.Once());
            Mocker.GetMock<IMoveMovieFiles>()
                .Verify(mover => mover.MoveMovieFile(It.IsAny<MovieFile>(), It.Is<LocalMovie>(local => local.Movie.Path == destination)), Times.Once());
        }

        [Test]
        public void should_leave_the_movie_path_when_the_profile_replaces_in_place()
        {
            GivenSingleMovieWithSingleMovieFile();

            var oldPath = _localMovie.Movie.Path;
            _localMovie.Movie.QualityProfile = new QualityProfile
            {
                UpgradeAllowed = true,
                UpgradeFilePlacement = UpgradeFilePlacement.ReplaceInCurrentLocation
            };

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            _localMovie.Movie.Path.Should().Be(oldPath);
            Mocker.GetMock<IMovieService>().Verify(service => service.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_keep_the_original_file_when_the_profile_moves_the_upgrade()
        {
            GivenSingleMovieWithSingleMovieFile();

            var destination = @"C:\Upgrades".AsOsAgnostic();
            var oldPath = _localMovie.Movie.Path;
            var originalFile = Path.Combine(oldPath, _localMovie.Movie.MovieFile.RelativePath);
            _localMovie.Movie.Id = 42;
            GivenMoveProfile(destination, keepOriginal: true);

            Mocker.GetMock<IBuildMoviePaths>()
                .Setup(builder => builder.BuildLibraryPath(_localMovie.Movie, destination))
                .Returns(destination);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(destination))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(It.IsAny<string>(), false))
                .Returns(new List<string> { originalFile, Path.Combine(oldPath, "A.Movie.2019.nfo") });

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetBestRootFolderPath(oldPath, null))
                .Returns(Directory.GetParent(oldPath).FullName);

            var result = Subject.UpgradeMovieFile(_movieFile, _localMovie);

            _localMovie.Movie.Path.Should().Be(destination);
            result.OldFiles.Should().ContainSingle();
            result.OldFiles[0].MovieFile.Should().BeSameAs(_localMovie.Movie.MovieFile);
            result.OldFiles[0].RecycleBinPath.Should().BeNull();
            Mocker.GetMock<IRecycleBinProvider>().Verify(bin => bin.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IKeptMovieFileRepository>()
                .Verify(repository => repository.Remember(42, originalFile), Times.Once());
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.UpgradeKept), Times.Once());
            Mocker.GetMock<IMoveMovieFiles>()
                .Verify(mover => mover.MoveMovieFile(It.IsAny<MovieFile>(), It.Is<LocalMovie>(local => local.Movie.Path == destination)), Times.Once());
        }

        [Test]
        public void should_delete_the_original_when_keep_original_is_off()
        {
            GivenSingleMovieWithSingleMovieFile();

            var destination = @"C:\Upgrades".AsOsAgnostic();
            GivenMoveProfile(destination, keepOriginal: false);

            Mocker.GetMock<IBuildMoviePaths>()
                .Setup(builder => builder.BuildLibraryPath(_localMovie.Movie, destination))
                .Returns(destination);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(destination))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(It.IsAny<string>(), false))
                .Returns(new List<string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetDirectories(It.IsAny<string>()))
                .Returns(new List<string>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetBestRootFolderPath(It.IsAny<string>(), null))
                .Returns(Directory.GetParent(_localMovie.Movie.Path).FullName);

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(bin => bin.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IKeptMovieFileRepository>().Verify(repository => repository.Remember(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_ignore_keep_original_when_the_upgrade_replaces_in_place()
        {
            GivenSingleMovieWithSingleMovieFile();
            _localMovie.Movie.QualityProfile = new QualityProfile
            {
                UpgradeAllowed = true,
                UpgradeFilePlacement = UpgradeFilePlacement.ReplaceInCurrentLocation,
                KeepOriginalFile = true
            };

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(bin => bin.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IKeptMovieFileRepository>().Verify(repository => repository.Remember(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_ignore_keep_original_when_upgrades_are_not_allowed()
        {
            GivenSingleMovieWithSingleMovieFile();
            _localMovie.Movie.QualityProfile = new QualityProfile
            {
                UpgradeAllowed = false,
                UpgradeFilePlacement = UpgradeFilePlacement.MoveToDesignatedLocation,
                UpgradeDestination = @"C:\Upgrades".AsOsAgnostic(),
                KeepOriginalFile = true
            };

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(bin => bin.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IKeptMovieFileRepository>().Verify(repository => repository.Remember(It.IsAny<int>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(service => service.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        private void GivenMoveProfile(string destination, bool keepOriginal)
        {
            _localMovie.Movie.QualityProfile = new QualityProfile
            {
                UpgradeAllowed = true,
                UpgradeFilePlacement = UpgradeFilePlacement.MoveToDesignatedLocation,
                UpgradeDestination = destination,
                KeepOriginalFile = keepOriginal
            };
        }
    }
}
