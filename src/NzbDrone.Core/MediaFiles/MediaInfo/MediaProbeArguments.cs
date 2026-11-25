using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    /// <summary>
    /// ffprobe input options. They have to come before <c>-i</c>. Options appended after the
    /// filename are ignored, and <c>-show_frames</c> then walks every frame of the file.
    /// </summary>
    public static class MediaProbeArguments
    {
        // A few megabytes of header, not the movie. 150MB probes and a 150 second
        // analyzeduration decode the file instead of reading its header.
        public const int ProbeSizeBytes = 5_000_000;
        public const int AnalyzeDurationMicroseconds = 5_000_000;

        public static IReadOnlyList<string> Format(string filename)
        {
            return new[]
            {
                "-loglevel", "error",
                "-probesize", ProbeSizeBytes.ToString(),
                "-analyzeduration", AnalyzeDurationMicroseconds.ToString(),
                "-i", filename,
                "-print_format", "json=compact=1",
                "-show_format",
                "-sexagesimal",
                "-show_streams",
                "-show_chapters"
            };
        }

        public static IReadOnlyList<string> OneFrame(string filename, int videoStreamIndex)
        {
            var index = videoStreamIndex < 0 ? 0 : videoStreamIndex;

            return new[]
            {
                "-loglevel", "error",
                "-probesize", ProbeSizeBytes.ToString(),
                "-analyzeduration", AnalyzeDurationMicroseconds.ToString(),
                "-read_intervals", "%+#1",
                "-i", filename,
                "-print_format", "json=compact=1",
                "-show_frames",
                "-select_streams", $"v:{index}"
            };
        }
    }
}
