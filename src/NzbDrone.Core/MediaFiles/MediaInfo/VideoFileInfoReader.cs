using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FFMpegCore;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IVideoFileInfoReader
    {
        MediaInfoModel GetMediaInfo(string filename);
        TimeSpan? GetRunTime(string filename);
    }

    public class VideoFileInfoReader : IVideoFileInfoReader
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly Dictionary<string, CachedProbe> _probeCache = new Dictionary<string, CachedProbe>();

        private sealed class CachedProbe
        {
            public long Size { get; set; }
            public DateTime WriteTime { get; set; }
            public MediaInfoModel Model { get; set; }
        }

        private sealed class FrameDump
        {
            public List<FrameItem> Frames { get; set; }
        }

        private sealed class FrameItem
        {
            [JsonPropertyName("side_data_list")]
            public List<Dictionary<string, JsonNode>> SideDataList { get; set; }
        }

        public const int MINIMUM_MEDIA_INFO_SCHEMA_REVISION = 16;
        public const int CURRENT_MEDIA_INFO_SCHEMA_REVISION = 16;

        private static readonly string[] ValidHdrColourPrimaries = { "bt2020" };
        private static readonly string[] HlgTransferFunctions = { "arib-std-b67" };
        private static readonly string[] PqTransferFunctions = { "smpte2084" };
        private static readonly string[] ValidHdrTransferFunctions = HlgTransferFunctions.Concat(PqTransferFunctions).ToArray();

        public VideoFileInfoReader(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;

            // We bundle ffprobe for all platforms
            GlobalFFOptions.Configure(options => options.BinaryFolder = AppDomain.CurrentDomain.BaseDirectory);
        }

        public MediaInfoModel GetMediaInfo(string filename)
        {
            if (!_diskProvider.FileExists(filename))
            {
                throw new FileNotFoundException("Media file does not exist: " + filename);
            }

            if (MediaFileExtensions.DiskExtensions
                .Concat(MediaFileExtensions.StreamingExtensions)
                .Contains(Path.GetExtension(filename)))
            {
                return null;
            }

            if (TryGetCached(filename, out var cached))
            {
                return cached;
            }

            try
            {
                _logger.Debug("Getting media info from {0}", filename);

                var analysis = FFProbe.FromJson(RunFfprobe(MediaProbeArguments.Format(filename)));
                var primaryVideoStream = GetPrimaryVideoStream(analysis);

                var mediaInfoModel = new MediaInfoModel();
                mediaInfoModel.ContainerFormat = analysis.Format.FormatName;
                mediaInfoModel.VideoFormat = primaryVideoStream?.CodecName;
                mediaInfoModel.VideoCodecID = primaryVideoStream?.CodecTagString;
                mediaInfoModel.VideoProfile = primaryVideoStream?.Profile;
                mediaInfoModel.VideoBitrate = GetBitrate(primaryVideoStream);
                mediaInfoModel.VideoMultiViewCount = primaryVideoStream?.Tags?.ContainsKey("stereo_mode") ?? false ? 2 : 1;
                mediaInfoModel.VideoBitDepth = GetPixelFormat(primaryVideoStream?.PixelFormat)?.Components.Min(x => x.BitDepth) ?? 8;
                mediaInfoModel.VideoColourPrimaries = primaryVideoStream?.ColorPrimaries;
                mediaInfoModel.VideoTransferCharacteristics = primaryVideoStream?.ColorTransfer;
                mediaInfoModel.Height = primaryVideoStream?.Height ?? 0;
                mediaInfoModel.Width = primaryVideoStream?.Width ?? 0;
                mediaInfoModel.RunTime = GetBestRuntime(analysis.PrimaryAudioStream?.Duration, primaryVideoStream?.Duration, analysis.Format.Duration);
                mediaInfoModel.VideoFps = primaryVideoStream?.RealFrameRate ?? 0;
                mediaInfoModel.ScanType = primaryVideoStream?.FieldOrder switch
                {
                    "tt" or "bb" or "tb" or "bt" => "Interlaced",
                    _ => "Progressive"
                };
                mediaInfoModel.RawStreamData = analysis.Json;

                mediaInfoModel.AudioStreams = analysis.AudioStreams?
                    .OrderBy(stream => stream.Index)
                    .Select(stream =>
                    {
                        var model = new MediaInfoAudioStreamModel
                        {
                            Language = stream.Language.IsNotNullOrWhiteSpace() ? stream.Language : "und",
                            Format = stream.CodecName,
                            CodecId = stream.CodecTagString,
                            Profile = stream.Profile,
                            Bitrate = GetBitrate(stream),
                            Channels = stream.Channels ?? 0,
                            ChannelPositions = stream.ChannelLayout
                        };

                        if ((stream.Tags?.TryGetValue("title", out var audioTitle) ?? false) && audioTitle.IsNotNullOrWhiteSpace())
                        {
                            model.Title = audioTitle.Trim();
                        }
                        else if ((stream.Tags?.TryGetValue("name", out var audioName) ?? false) && audioName.IsNotNullOrWhiteSpace())
                        {
                            model.Title = audioName.Trim();
                        }

                        return  model;
                    })
                    .ToList();

                mediaInfoModel.SubtitleStreams = analysis.SubtitleStreams?
                    .Where(stream => stream.Language.IsNotNullOrWhiteSpace())
                    .OrderBy(stream => stream.Index)
                    .Select(stream =>
                    {
                        var model = new MediaInfoSubtitleStreamModel
                        {
                            Language = stream.Language,
                            Format = stream.CodecName,
                        };

                        if ((stream.Tags?.TryGetValue("title", out var subtitleTitle) ?? false) && subtitleTitle.IsNotNullOrWhiteSpace())
                        {
                            model.Title = subtitleTitle.Trim();
                        }
                        else if ((stream.Tags?.TryGetValue("name", out var subtitleName) ?? false) && subtitleName.IsNotNullOrWhiteSpace())
                        {
                            model.Title = subtitleName.Trim();
                        }

                        if (stream.Disposition?.TryGetValue("forced", out var forcedSubtitle) ?? false)
                        {
                            model.Forced = forcedSubtitle;
                        }

                        if (stream.Disposition?.TryGetValue("hearing_impaired", out var hearingImpairedSubtitle) ?? false)
                        {
                            model.HearingImpaired = hearingImpairedSubtitle;
                        }

                        return  model;
                    })
                    .ToList();

                mediaInfoModel.SchemaRevision = CURRENT_MEDIA_INFO_SCHEMA_REVISION;

                if (analysis.Format.Tags?.TryGetValue("title", out var title) ?? false)
                {
                    mediaInfoModel.Title = title;
                }

                IReadOnlyList<IReadOnlyDictionary<string, JsonNode>> framesSideData = new List<IReadOnlyDictionary<string, JsonNode>>();

                // One frame is enough to tell HDR10 from HDR10+. The interval has to be an input
                // option, or ffprobe describes every frame and reads the whole movie.
                if (PqTransferFunctions.Contains(mediaInfoModel.VideoTransferCharacteristics))
                {
                    var videoStreamIndex = analysis.VideoStreams.ToList().FindIndex(stream => stream.Index == primaryVideoStream?.Index);
                    framesSideData = ReadFirstFrameSideData(filename, videoStreamIndex);
                }

                var streamSideData = primaryVideoStream?.SideData ?? new List<IReadOnlyDictionary<string, JsonNode>>().AsReadOnly();

                var sideData = streamSideData.Concat(framesSideData).ToList();
                mediaInfoModel.VideoHdrFormat = GetHdrFormat(mediaInfoModel.VideoBitDepth, mediaInfoModel.VideoColourPrimaries, mediaInfoModel.VideoTransferCharacteristics, sideData);

                StoreCache(filename, mediaInfoModel);
                return mediaInfoModel;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to parse media info from file: {0}", filename);
            }

            return null;
        }

        public TimeSpan? GetRunTime(string filename)
        {
            var info = GetMediaInfo(filename);

            return info?.RunTime;
        }

        private bool TryGetCached(string filename, out MediaInfoModel model)
        {
            model = null;

            if (!TryGetCacheKey(filename, out var size, out var writeTime))
            {
                return false;
            }

            lock (_probeCache)
            {
                if (_probeCache.TryGetValue(filename, out var cached) &&
                    cached.Size == size &&
                    cached.WriteTime == writeTime)
                {
                    model = cached.Model;
                    return true;
                }
            }

            return false;
        }

        private void StoreCache(string filename, MediaInfoModel model)
        {
            if (!TryGetCacheKey(filename, out var size, out var writeTime))
            {
                return;
            }

            lock (_probeCache)
            {
                if (_probeCache.Count > 128)
                {
                    _probeCache.Clear();
                }

                _probeCache[filename] = new CachedProbe
                {
                    Size = size,
                    WriteTime = writeTime,
                    Model = model
                };
            }
        }

        private bool TryGetCacheKey(string filename, out long size, out DateTime writeTime)
        {
            size = 0;
            writeTime = DateTime.MinValue;

            try
            {
                size = _diskProvider.GetFileSize(filename);
                writeTime = _diskProvider.FileGetLastWrite(filename);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private IReadOnlyList<IReadOnlyDictionary<string, JsonNode>> ReadFirstFrameSideData(string filename, int videoStreamIndex)
        {
            try
            {
                var json = RunFfprobe(MediaProbeArguments.OneFrame(filename, videoStreamIndex));
                var dump = JsonSerializer.Deserialize<FrameDump>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                var sideData = dump?.Frames?.FirstOrDefault()?.SideDataList;

                if (sideData == null)
                {
                    return new List<IReadOnlyDictionary<string, JsonNode>>();
                }

                return sideData.Cast<IReadOnlyDictionary<string, JsonNode>>().ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read a single frame of side data from {0}", filename);
                return new List<IReadOnlyDictionary<string, JsonNode>>();
            }
        }

        private static string RunFfprobe(IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo(GlobalFFOptions.GetFFProbeBinaryPath())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(60_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // The probe is already being torn down.
                }

                throw new TimeoutException("ffprobe did not finish within 60 seconds");
            }

            Task.WaitAll(stdout, stderr);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(stderr.Result);
            }

            return stdout.Result;
        }

        private static TimeSpan GetBestRuntime(TimeSpan? audio, TimeSpan? video, TimeSpan general)
        {
            if (!video.HasValue || video.Value.TotalMilliseconds == 0)
            {
                if (!audio.HasValue || audio.Value.TotalMilliseconds == 0)
                {
                    return general;
                }

                return audio.Value;
            }

            return video.Value;
        }

        private static long GetBitrate(MediaStream mediaStream)
        {
            if (mediaStream?.BitRate is > 0)
            {
                return mediaStream.BitRate.Value;
            }

            if ((mediaStream?.Tags?.TryGetValue("BPS", out var bitratePerSecond) ?? false) && bitratePerSecond.IsNotNullOrWhiteSpace())
            {
                return Convert.ToInt64(bitratePerSecond);
            }

            return 0;
        }

        private static VideoStream GetPrimaryVideoStream(IMediaAnalysis mediaAnalysis)
        {
            if (mediaAnalysis.VideoStreams.Count <= 1)
            {
                return mediaAnalysis.PrimaryVideoStream;
            }

            // motion image codec streams are often in front of the main video stream
            var codecFilter = new[] { "mjpeg", "png" };

            return mediaAnalysis.VideoStreams.FirstOrDefault(s => !codecFilter.Contains(s.CodecName)) ?? mediaAnalysis.PrimaryVideoStream;
        }

        private static FFProbePixelFormat GetPixelFormat(string format)
        {
            if (format.IsNullOrWhiteSpace())
            {
                return null;
            }

            return FFProbe.TryGetPixelFormat(format, out var pixelFormat) ? pixelFormat : null;
        }

        public static HdrFormat GetHdrFormat(int bitDepth, string colorPrimaries, string transferFunction, IReadOnlyList<IReadOnlyDictionary<string, JsonNode>> sideData)
        {
            if (bitDepth < 10)
            {
                return HdrFormat.None;
            }

            if (TryGetSideData(sideData, FFMpegCoreSideDataTypes.DoviConfigurationRecordSideData, out var dovi))
            {
                var hasHdr10Plus = TryGetSideData(sideData, FFMpegCoreSideDataTypes.HdrDynamicMetadataSpmte2094, out _);

                dovi.TryGetValue("dv_bl_signal_compatibility_id", out var dvBlSignalCompatibilityId);

                return dvBlSignalCompatibilityId?.GetValue<int>() switch
                {
                    1 => hasHdr10Plus ? HdrFormat.DolbyVisionHdr10Plus : HdrFormat.DolbyVisionHdr10,
                    2 => HdrFormat.DolbyVisionSdr,
                    4 => HdrFormat.DolbyVisionHlg,
                    6 => hasHdr10Plus ? HdrFormat.DolbyVisionHdr10Plus : HdrFormat.DolbyVisionHdr10,
                    _ => HdrFormat.DolbyVision
                };
            }

            if (!ValidHdrColourPrimaries.Contains(colorPrimaries) || !ValidHdrTransferFunctions.Contains(transferFunction))
            {
                return HdrFormat.None;
            }

            if (HlgTransferFunctions.Contains(transferFunction))
            {
                return HdrFormat.Hlg10;
            }

            if (PqTransferFunctions.Contains(transferFunction))
            {
                if (TryGetSideData(sideData, FFMpegCoreSideDataTypes.HdrDynamicMetadataSpmte2094, out _))
                {
                    return HdrFormat.Hdr10Plus;
                }

                if (TryGetSideData(sideData, FFMpegCoreSideDataTypes.MasteringDisplayMetadata, out _) ||
                    TryGetSideData(sideData, FFMpegCoreSideDataTypes.ContentLightLevelMetadata, out _))
                {
                    return HdrFormat.Hdr10;
                }

                return HdrFormat.Pq10;
            }

            return HdrFormat.None;
        }

        private static bool TryGetSideData(IReadOnlyList<IReadOnlyDictionary<string, JsonNode>> list, string name, out IReadOnlyDictionary<string, JsonNode> result)
        {
            result = list?.FirstOrDefault(item =>
                item.TryGetValue("side_data_type", out var rawSideDataType) &&
                rawSideDataType.GetValue<string>().Equals(name, StringComparison.OrdinalIgnoreCase));

            return result != null;
        }
    }
}
