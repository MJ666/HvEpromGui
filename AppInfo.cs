using System;
using System.Reflection;

namespace HvEpromGui
{
    internal static class AppInfo
    {
        public const string Name = "HV-EPROM Programmer";

        /// <summary>Version from the project file (&lt;Version&gt; in HvEpromGui.csproj), e.g. "1.0.0".</summary>
        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "?" : v.ToString(3);
            }
        }
    }
}
