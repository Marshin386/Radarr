using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Aggregation
{
    [TestFixture]
    public class AggregationServiceFixture : CoreTest<AggregationService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IEnumerable<IAggregateLocalMovie>>(new List<IAggregateLocalMovie>());
        }

        [Test]
        public void should_not_read_an_existing_library_file_when_analysis_is_off()
        {
            var folder = @"C:\Movies".AsOsAgnostic();
            var file = Path.Combine(folder, "Example (2020).mkv");

            Mocker.GetMock<IConfigService>()
                  .Setup(config => config.EnableMediaInfo)
                  .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(disk => disk.GetFileSize(file))
                  .Returns(10);

            var localMovie = new LocalMovie
            {
                Path = file,
                Movie = new Movie { Path = folder, Title = "Example" },
                ExistingFile = true,
                FileMovieInfo = new ParsedMovieInfo
                {
                    MovieTitles = new List<string> { "Example" },
                    Year = 2020
                }
            };

            Subject.Augment(localMovie, null);

            localMovie.Size.Should().Be(10);
            Mocker.GetMock<IVideoFileInfoReader>()
                  .Verify(reader => reader.GetMediaInfo(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(disk => disk.OpenReadStream(It.IsAny<string>()), Times.Never());
        }
    }
}
