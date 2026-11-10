using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        public static NamingConfig Default => new NamingConfig
        {
            RenameMovies = false,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            MovieFolderFormat = "{Movie Title} ({Release Year})",
            CustomMovieFolderFormat = "{Movie Collection}/{Movie Title} ({Release Year})",
            StandardMovieFormat = "{Movie Title} ({Release Year}) {Quality Full}",
        };

        public bool RenameMovies { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardMovieFormat { get; set; }
        public string MovieFolderFormat { get; set; }

        /// <summary>
        /// Folder pattern used when a root folder's layout is <c>Custom</c>.
        /// Slashes create nested directories. Empty means new movies are placed in the root.
        /// </summary>
        public string CustomMovieFolderFormat { get; set; }
    }
}
