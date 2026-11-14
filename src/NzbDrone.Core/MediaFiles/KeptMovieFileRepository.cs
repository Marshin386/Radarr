using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IKeptMovieFileRepository : IBasicRepository<KeptMovieFile>
    {
        List<KeptMovieFile> GetByMovieId(int movieId);
        void Remember(int movieId, string path);
    }

    public class KeptMovieFileRepository : BasicRepository<KeptMovieFile>, IKeptMovieFileRepository
    {
        public KeptMovieFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<KeptMovieFile> GetByMovieId(int movieId)
        {
            return Query(file => file.MovieId == movieId);
        }

        public void Remember(int movieId, string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return;
            }

            var existing = GetByMovieId(movieId);

            if (existing.Any(file => file.Path.PathEquals(path)))
            {
                return;
            }

            Insert(new KeptMovieFile
            {
                MovieId = movieId,
                Path = path
            });
        }
    }
}
