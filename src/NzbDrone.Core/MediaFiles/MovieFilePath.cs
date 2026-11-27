namespace NzbDrone.Core.MediaFiles
{
    /// <summary>
    /// Movie id and relative path only. Shared-folder scans use this so they do not load media info.
    /// </summary>
    public class MovieFilePath
    {
        public int MovieId { get; set; }
        public string RelativePath { get; set; }
    }
}
