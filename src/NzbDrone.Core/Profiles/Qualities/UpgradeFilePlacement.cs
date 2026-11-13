namespace NzbDrone.Core.Profiles.Qualities
{
    public enum UpgradeFilePlacement
    {
        /// <summary>
        /// Import the upgrade into the movie's current folder and replace the old file.
        /// </summary>
        ReplaceInCurrentLocation = 0,

        /// <summary>
        /// Place the upgrade under <see cref="QualityProfile.UpgradeDestination"/> using the library structure.
        /// </summary>
        MoveToDesignatedLocation = 1
    }
}
