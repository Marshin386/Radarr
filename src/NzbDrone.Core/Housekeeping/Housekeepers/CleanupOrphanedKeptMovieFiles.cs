using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedKeptMovieFiles : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedKeptMovieFiles(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""KeptMovieFiles""
                             WHERE ""Id"" IN (
                             SELECT ""KeptMovieFiles"".""Id"" FROM ""KeptMovieFiles""
                             LEFT OUTER JOIN ""Movies""
                             ON ""KeptMovieFiles"".""MovieId"" = ""Movies"".""Id""
                             WHERE ""Movies"".""Id"" IS NULL)");
        }
    }
}
