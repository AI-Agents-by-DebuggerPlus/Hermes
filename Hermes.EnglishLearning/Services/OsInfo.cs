using System;
using System.Runtime.InteropServices;

namespace Hermes.EnglishLearning.Services;

internal static class OsInfo
{
    private static readonly Lazy<Version> RealVersion = new(ReadRealVersion);

    public static Version Version => RealVersion.Value;

    /// <summary>Windows 7 / Server 2008 R2 and older — avoid WPF MediaPlayer + MP3.</summary>
    public static bool IsWindows7OrOlder
    {
        get
        {
            var v = Version;
            return v.Major < 6 || (v.Major == 6 && v.Minor <= 1);
        }
    }

    public static string Describe()
    {
        var v = Version;
        string name;
        if (v.Major == 5 && v.Minor == 1) name = "Windows XP";
        else if (v.Major == 6 && v.Minor <= 1) name = v.Minor == 0 ? "Windows Vista" : "Windows 7";
        else if (v.Major == 6 && v.Minor == 2) name = "Windows 8";
        else if (v.Major == 6 && v.Minor == 3) name = "Windows 8.1";
        else if (v.Major >= 10 && v.Build >= 22000) name = "Windows 11 (build " + v.Build + ")";
        else if (v.Major >= 10) name = "Windows 10 (build " + v.Build + ")";
        else name = "Windows " + v;

        return name
               + "; CLR " + Environment.Version
               + "; " + (Environment.Is64BitProcess ? "64-bit process" : "32-bit process")
               + "; OS " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")
               + "; machine=" + Environment.MachineName;
    }

    private static Version ReadRealVersion()
    {
        try
        {
            var info = new OsVersionInfo { OSVersionInfoSize = Marshal.SizeOf(typeof(OsVersionInfo)) };
            if (RtlGetVersion(ref info) == 0)
            {
                return new Version((int)info.MajorVersion, (int)info.MinorVersion, (int)info.BuildNumber);
            }
        }
        catch
        {
        }

        return Environment.OSVersion.Version;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlGetVersion(ref OsVersionInfo versionInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public int OSVersionInfoSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CSDVersion;
    }
}
