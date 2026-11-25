namespace NzbDrone.Core.Profiles.Qualities
{
    public enum UpgradeFilePlacement
    {
        /// <summary>
        /// Import the file into the movie's current folder and replace the old file when one exists.
        /// </summary>
        ReplaceInCurrentLocation = 0,

        /// <summary>
        /// Place every new download under <see cref="QualityProfile.UpgradeDestination"/> using the library structure.
        /// This includes a replacement, the first file, and a new file after the current one was deleted.
        /// </summary>
        MoveToDesignatedLocation = 1
    }
}
