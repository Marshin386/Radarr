using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(246)]
    public class add_upgrade_file_placement : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles")
                .AddColumn("UpgradeFilePlacement").AsInt32().WithDefaultValue(0);

            Alter.Table("QualityProfiles")
                .AddColumn("UpgradeDestination").AsString().Nullable();
        }
    }
}
