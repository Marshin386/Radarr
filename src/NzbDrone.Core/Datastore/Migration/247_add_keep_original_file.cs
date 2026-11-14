using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(247)]
    public class add_keep_original_file : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles")
                .AddColumn("KeepOriginalFile").AsBoolean().WithDefaultValue(false);

            Create.TableForModel("KeptMovieFiles")
                .WithColumn("MovieId").AsInt32().NotNullable().Indexed()
                .WithColumn("Path").AsString().NotNullable();
        }
    }
}
