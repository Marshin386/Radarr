using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class BuildInfoFixture
    {
        [Test]
        public void should_return_version()
        {
            BuildInfo.Version.Major.Should().BeOneOf(7, 10);
        }

        [Test]
        public void should_report_fork_version()
        {
            BuildInfo.VersionString.Should().Be("7.0.3-folders.2");
        }

        [Test]
        public void should_get_branch()
        {
            BuildInfo.Branch.Should().NotBe("unknown");
            BuildInfo.Branch.Should().NotBeNullOrWhiteSpace();
        }
    }
}
