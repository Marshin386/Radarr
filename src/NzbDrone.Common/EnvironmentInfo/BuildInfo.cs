using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NzbDrone.Common.EnvironmentInfo
{
    public static class BuildInfo
    {
        static BuildInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();

            Version = assembly.GetName().Version;

            var attributes = assembly.GetCustomAttributes(true);

            Branch = "unknown";

            var config = attributes.OfType<AssemblyConfigurationAttribute>().FirstOrDefault();
            if (config != null)
            {
                Branch = config.Configuration;
            }

            var informational = attributes.OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+', StringComparison.Ordinal);
                if (plus >= 0)
                {
                    informational = informational.Substring(0, plus);
                }
            }

            VersionString = string.IsNullOrWhiteSpace(informational) ? Version.ToString() : informational;
            Release = $"{VersionString}-{Branch}";
        }

        public static string AppName { get; } = "Radarr";

        public static Version Version { get; }

        /// <summary>
        /// User-facing version. Fork builds use the informational version, for example 7.0.3-folders.2.
        /// </summary>
        public static string VersionString { get; }
        public static string Branch { get; }
        public static string Release { get; }

        public static DateTime BuildDateTime
        {
            get
            {
                var fileLocation = Assembly.GetCallingAssembly().Location;
                return new FileInfo(fileLocation).LastWriteTimeUtc;
            }
        }

        public static bool IsDebug
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }
    }
}
