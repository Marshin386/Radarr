using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Movies.Events
{
    public class MoviesDeletedEvent : IEvent
    {
        public List<Movie> Movies { get; private set; }
        public bool DeleteFiles { get; private set; }
        public bool AddImportListExclusion { get; private set; }

        /// <summary>
        /// Relative paths captured before the movie rows are removed. Shared-folder deletes use this
        /// so a parallel database cleanup cannot forget which files belonged to the movie.
        /// </summary>
        public Dictionary<int, List<string>> OwnedRelativePaths { get; set; }

        public MoviesDeletedEvent(List<Movie> movies, bool deleteFiles, bool addImportListExclusion)
        {
            Movies = movies;
            DeleteFiles = deleteFiles;
            AddImportListExclusion = addImportListExclusion;
        }
    }
}
