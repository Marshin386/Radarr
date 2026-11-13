namespace NzbDrone.Core.Movies
{
    public enum MovieFolderLayout
    {
        /// <summary>
        /// One dedicated folder per movie. This is the historical Radarr layout.
        /// </summary>
        MovieFolder = 0,

        /// <summary>
        /// Movie files live directly in the root folder. Many movies share that folder.
        /// </summary>
        Flat = 1,

        /// <summary>
        /// Movies may live in any nested directory under the root, and several movies may share a directory.
        /// </summary>
        Custom = 2
    }
}
