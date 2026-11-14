using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles
{
    /// <summary>
    /// A lower-quality file left on disk when an upgrade was moved and the profile
    /// asked to keep the original. The path is no longer the movie's tracked file.
    /// </summary>
    public class KeptMovieFile : ModelBase
    {
        public int MovieId { get; set; }
        public string Path { get; set; }
    }
}
