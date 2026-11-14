namespace NzbDrone.Core.MediaFiles
{
    public enum DeleteMediaFileReason
    {
        MissingFromDisk,
        Manual,
        Upgrade,
        NoLinkedEpisodes,
        ManualOverride,

        /// <summary>
        /// The movie file row was detached because a better file was imported, and the
        /// original was left on disk. Extras and notifications must not treat it as deleted.
        /// </summary>
        UpgradeKept
    }
}
