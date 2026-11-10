using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(244)]
    public class add_movie_folder_layouts : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RootFolders").AddColumn("FolderLayout").AsInt32().WithDefaultValue(0);

            Alter.Table("NamingConfig")
                .AddColumn("CustomMovieFolderFormat")
                .AsString()
                .Nullable()
                .WithDefaultValue("{Movie Collection}/{Movie Title} ({Release Year})");

            Update.Table("NamingConfig")
                .Set(new { CustomMovieFolderFormat = "{Movie Collection}/{Movie Title} ({Release Year})" })
                .AllRows();
        }
    }
}
