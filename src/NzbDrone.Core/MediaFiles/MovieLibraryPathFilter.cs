using System.IO;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles
{
    /// <summary>
    /// The same exclusions DiskScanService applies while scanning a movie folder.
    /// Shared so root-folder discovery of nested files skips extras, samples, and hidden folders.
    /// </summary>
    public static class MovieLibraryPathFilter
    {
        private static readonly Regex ExcludedExtrasSubFolderRegex = new Regex(@"(?:\\|\/|^)(?:extras|extrafanart|behind the scenes|deleted scenes|featurettes|interviews|other|scenes|sample[s]?|shorts|trailers)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcludedSubFoldersRegex = new Regex(@"(?:\\|\/|^)(?:@eadir|\.@__thumb|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcludedExtraFilesRegex = new Regex(@"(-(trailer|other|behindthescenes|deleted|featurette|interview|scene|short)\.[^.]+$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcludedFilesRegex = new Regex(@"^\.(_|unmanic|DS_Store$)|^Thumbs\.db$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool ShouldIgnore(string basePath, string path, bool filterExtras = true)
        {
            var relativePath = basePath.GetRelativePath(path);
            var fileName = Path.GetFileName(path);

            if (ExcludedSubFoldersRegex.IsMatch(relativePath) || ExcludedFilesRegex.IsMatch(fileName))
            {
                return true;
            }

            if (!filterExtras)
            {
                return false;
            }

            return ExcludedExtrasSubFolderRegex.IsMatch(relativePath) || ExcludedExtraFilesRegex.IsMatch(fileName);
        }
    }
}
