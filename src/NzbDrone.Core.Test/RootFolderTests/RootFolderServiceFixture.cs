using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class RootFolderServiceFixture : CoreTest<RootFolderService>
    {
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<RootFolder>());

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig())
                  .Returns(_namingConfig);
        }

        private void WithNonExistingFolder()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(m => m.FolderExists(It.IsAny<string>()))
                .Returns(false);
        }

        [TestCase("D:\\TV Shows\\")]
        [TestCase("//server//folder")]
        public void should_be_able_to_add_root_dir(string path)
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string>());

            var root = new RootFolder { Path = path.AsOsAgnostic() };

            Subject.Add(root);

            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Insert(root), Times.Once());
        }

        [Test]
        public void should_throw_if_folder_being_added_doesnt_exist()
        {
            WithNonExistingFolder();

            Assert.Throws<DirectoryNotFoundException>(() => Subject.Add(new RootFolder { Path = "C:\\TEST".AsOsAgnostic() }));
        }

        [Test]
        public void should_be_able_to_remove_root_dir()
        {
            Subject.Remove(1);
            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Delete(1), Times.Once());
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("BAD PATH")]
        public void invalid_folder_path_throws_on_add(string path)
        {
            Assert.Throws<ArgumentException>(() =>
                    Mocker.Resolve<RootFolderService>().Add(new RootFolder { Id = 0, Path = path }));
        }

        [Test]
        public void adding_duplicated_root_folder_should_throw()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(c => c.All()).Returns(new List<RootFolder> { new RootFolder { Path = "C:\\TV".AsOsAgnostic() } });

            Assert.Throws<InvalidOperationException>(() => Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }

        [Test]
        public void should_throw_when_adding_not_writable_folder()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(false);

            Assert.Throws<UnauthorizedAccessException>(() => Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }

        [TestCase("$recycle.bin")]
        [TestCase("system volume information")]
        [TestCase("recycler")]
        [TestCase("lost+found")]
        [TestCase(".appledb")]
        [TestCase(".appledesktop")]
        [TestCase(".appledouble")]
        [TestCase("@eadir")]
        [TestCase(".grab")]
        public void should_get_root_folder_with_subfolders_excluding_special_sub_folders(string subFolder)
        {
            var rootFolder = Builder<RootFolder>.CreateNew()
                                                .With(r => r.Path = @"C:\Test\TV")
                                                .Build();
            if (OsInfo.IsNotWindows)
            {
                rootFolder = Builder<RootFolder>.CreateNew()
                                                .With(r => r.Path = @"/Test/TV")
                                                .Build();
            }

            var subFolders = new[]
                        {
                            "Series1",
                            "Series2",
                            "Series3",
                            subFolder
                        };

            var folders = subFolders.Select(f => Path.Combine(@"C:\Test\TV", f)).ToArray();

            if (OsInfo.IsNotWindows)
            {
                folders = subFolders.Select(f => Path.Combine(@"/Test/TV", f)).ToArray();
            }

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(rootFolder.Path))
                  .Returns(folders);

            var unmappedFolders = Subject.Get(rootFolder.Id, true).UnmappedFolders;

            unmappedFolders.Count.Should().BeGreaterThan(0);
            unmappedFolders.Should().NotContain(u => u.Name == subFolder);
        }

        [TestCase("")]
        [TestCase(null)]
        public void should_handle_non_configured_recycle_bin(string recycleBinPath)
        {
            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.Path = @"C:\Test\TV")
                .Build();
            if (OsInfo.IsNotWindows)
            {
                rootFolder = Builder<RootFolder>.CreateNew()
                    .With(r => r.Path = @"/Test/TV")
                    .Build();
            }

            var subFolders = new[]
            {
                "Series1",
                "Series2",
                "Series3"
            };

            var folders = subFolders.Select(f => Path.Combine(@"C:\Test\TV", f)).ToArray();

            if (OsInfo.IsNotWindows)
            {
                folders = subFolders.Select(f => Path.Combine(@"/Test/TV", f)).ToArray();
            }

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.RecycleBin)
                .Returns(recycleBinPath);

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                .Setup(s => s.AllMoviePaths())
                .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(rootFolder.Path))
                .Returns(folders);

            var unmappedFolders = Subject.Get(rootFolder.Id, true).UnmappedFolders;

            unmappedFolders.Count.Should().Be(3);
        }

        [Test]
        public void should_exclude_recycle_bin()
        {
            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.Path = @"C:\Test\TV")
                .Build();

            if (OsInfo.IsNotWindows)
            {
                rootFolder = Builder<RootFolder>.CreateNew()
                    .With(r => r.Path = @"/Test/TV")
                    .Build();
            }

            var subFolders = new[]
            {
                "Series1",
                "Series2",
                "Series3",
                "BIN"
            };

            var folders = subFolders.Select(f => Path.Combine(@"C:\Test\TV", f)).ToArray();

            if (OsInfo.IsNotWindows)
            {
                folders = subFolders.Select(f => Path.Combine(@"/Test/TV", f)).ToArray();
            }

            var recycleFolder = Path.Combine(OsInfo.IsNotWindows ? @"/Test/TV" : @"C:\Test\TV", "BIN");

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.RecycleBin)
                .Returns(recycleFolder);

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                .Setup(s => s.AllMoviePaths())
                .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(rootFolder.Path))
                .Returns(folders);

            var unmappedFolders = Subject.Get(rootFolder.Id, true).UnmappedFolders;

            unmappedFolders.Count.Should().Be(3);
            unmappedFolders.Should().NotContain(u => u.Name == "BIN");
        }

        [Test]
        public void should_get_unmapped_folders_inside_letter_subfolder()
        {
            _namingConfig.MovieFolderFormat = "{Movie TitleFirstCharacter}\\{Movie Title}".AsOsAgnostic();

            var rootFolderPath = @"C:\Test\Movies".AsOsAgnostic();
            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.Path = rootFolderPath)
                .Build();

            var subFolderPath = Path.Combine(rootFolderPath, "M");

            var subFolders = new[]
            {
                "Movie1",
                "Movie2",
                "Movie3",
            };

            var folders = subFolders.Select(f => Path.Combine(subFolderPath, f)).ToArray();

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                .Setup(s => s.AllMoviePaths())
                .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(rootFolder.Path))
                .Returns(new[] { subFolderPath });

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(subFolderPath))
                .Returns(folders);

            var unmappedFolders = Subject.Get(rootFolder.Id, false).UnmappedFolders;

            unmappedFolders.Count.Should().Be(3);
        }

        [Test]
        public void should_list_each_video_file_in_a_flat_root()
        {
            var rootFolderPath = @"C:\Movies".AsOsAgnostic();
            var rootFolder = new RootFolder
            {
                Id = 4,
                Path = rootFolderPath,
                FolderLayout = MovieFolderLayout.Flat
            };

            var files = new[]
            {
                Path.Combine(rootFolderPath, "Nocturnal Animals (2016).mkv"),
                Path.Combine(rootFolderPath, "Heat (1995).mkv")
            };

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(repository => repository.All())
                .Returns(new List<RootFolder> { rootFolder });

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(repository => repository.Get(rootFolder.Id))
                .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                .Setup(repository => repository.AllMoviePaths())
                .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(rootFolderPath, false))
                .Returns(files);

            var unmapped = Subject.Get(rootFolder.Id, false).UnmappedFolders;

            unmapped.Should().HaveCount(2);
            unmapped.Select(item => item.Path).Should().OnlyContain(path => path == rootFolderPath);
            unmapped.Select(item => item.Name).Should().BeEquivalentTo("Nocturnal Animals (2016)", "Heat (1995)");
        }

        [Test]
        public void should_import_movies_nested_under_arbitrary_folders()
        {
            var rootFolderPath = @"C:\Theatre".AsOsAgnostic();
            var marvelFolder = Path.Combine(rootFolderPath, "Movie Series", "Marvel Movies");
            var rootFolder = new RootFolder
            {
                Id = 5,
                Path = rootFolderPath,
                FolderLayout = MovieFolderLayout.Custom
            };

            var ironMan = Path.Combine(marvelFolder, "(2008) Iron Man.mkv");
            var heat = Path.Combine(rootFolderPath, "Movies", "Heat (1995)", "Heat (1995).mkv");

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(repository => repository.All())
                .Returns(new List<RootFolder> { rootFolder });

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(repository => repository.Get(rootFolder.Id))
                .Returns(rootFolder);

            Mocker.GetMock<IMovieRepository>()
                .Setup(repository => repository.AllMoviePaths())
                .Returns(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(rootFolderPath, true))
                .Returns(new[] { ironMan, heat });

            var unmapped = Subject.Get(rootFolder.Id, false).UnmappedFolders;

            unmapped.Should().HaveCount(2);
            unmapped.Should().Contain(item => item.Path == marvelFolder && item.RelativePath == rootFolderPath.GetRelativePath(ironMan));
            unmapped.Should().Contain(item => item.Path == Path.GetDirectoryName(heat));
        }

        [Test]
        public void should_persist_a_layout_change_on_an_existing_root()
        {
            var stored = new RootFolder
            {
                Id = 7,
                Path = @"C:\Movies".AsOsAgnostic(),
                FolderLayout = MovieFolderLayout.MovieFolder
            };

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(repository => repository.Get(stored.Id))
                .Returns(stored);

            Subject.Update(new RootFolder
            {
                Id = stored.Id,
                FolderLayout = MovieFolderLayout.Flat
            });

            stored.FolderLayout.Should().Be(MovieFolderLayout.Flat);
            Mocker.GetMock<IRootFolderRepository>()
                .Verify(repository => repository.SetFields(stored, It.IsAny<Expression<Func<RootFolder, object>>[]>()), Times.Once());
        }
    }
}
