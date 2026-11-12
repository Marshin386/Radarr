using NzbDrone.Core.Datastore;
using NzbDrone.Core.Movies;

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
            FolderLayout = MovieFolderLayout.MovieFolder,
        };

        public bool RenameMovies { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardMovieFormat { get; set; }
        public string MovieFolderFormat { get; set; }

        /// <summary>
        /// Folder pattern used when <see cref="FolderLayout"/> is <see cref="MovieFolderLayout.Custom"/>.
        /// Slashes create nested directories. Empty means new movies are placed in the root.
        /// </summary>
        public string CustomMovieFolderFormat { get; set; }

        /// <summary>
        /// Library-wide folder structure. Saving this value is copied onto every root folder.
        /// </summary>
        public MovieFolderLayout FolderLayout { get; set; }
    }
}
