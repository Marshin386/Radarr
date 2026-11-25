using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.Test.MediaFiles.MediaInfo
{
    [TestFixture]
    public class MediaProbeArgumentsFixture
    {
        [Test]
        public void format_probe_limits_the_read_before_opening_the_file()
        {
            var args = MediaProbeArguments.Format("library/Example (2020).mkv").ToList();
            var input = args.IndexOf("-i");

            input.Should().BeGreaterThan(0);
            args.Take(input).Should().Contain("-probesize");
            args.Take(input).Should().Contain("-analyzeduration");
            args.Skip(input + 1).Should().NotContain("-probesize");
            args.Skip(input + 1).Should().NotContain("-analyzeduration");
            args.Should().NotContain("-show_frames");
            int.Parse(args[args.IndexOf("-probesize") + 1]).Should().BeLessOrEqualTo(8_000_000);
            int.Parse(args[args.IndexOf("-analyzeduration") + 1]).Should().BeLessOrEqualTo(10_000_000);
            args[input + 1].Should().Be("library/Example (2020).mkv");
        }

        [Test]
        public void frame_probe_reads_one_interval_before_opening_the_file()
        {
            var args = MediaProbeArguments.OneFrame("library/Example (2020).mkv", 0).ToList();
            var input = args.IndexOf("-i");

            args.Take(input).Should().Contain("-read_intervals");
            args.Take(input).Should().NotContain("-show_frames");
            args.Skip(input + 1).Should().Contain("-show_frames");
            args[args.IndexOf("-read_intervals") + 1].Should().Be("%+#1");
            int.Parse(args[args.IndexOf("-analyzeduration") + 1]).Should().BeLessOrEqualTo(10_000_000);
        }
    }
}
