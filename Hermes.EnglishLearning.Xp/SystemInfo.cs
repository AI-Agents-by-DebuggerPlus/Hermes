using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Hermes.EnglishLearning.Xp;

/// <summary>Human-readable OS / machine identity for startup logs.</summary>
internal static class SystemInfo
{
    public static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(FriendlyOsName());
        try
        {
            sb.Append(" (").Append(Environment.OSVersion).Append(')');
        }
        catch
        {
            // ignore
        }

        sb.Append("; CLR ").Append(Environment.Version);
        sb.Append("; ").Append(Environment.Is64BitProcess ? "64-bit process" : "32-bit process");
        try
        {
            sb.Append("; machine=").Append(Environment.MachineName);
        }
        catch
        {
            // ignore
        }

        return sb.ToString();
    }

    public static string FriendlyOsName()
    {
        try
        {
            var v = Environment.OSVersion.Version;
            if (v.Major == 5 && v.Minor == 1) return "Windows XP";
            if (v.Major == 5 && v.Minor == 2) return "Windows Server 2003 / XP x64";
            if (v.Major == 6 && v.Minor == 0) return "Windows Vista / Server 2008";
            if (v.Major == 6 && v.Minor == 1) return "Windows 7 / Server 2008 R2";
            if (v.Major == 6 && v.Minor == 2) return FriendlyModernWindows();
            if (v.Major == 6 && v.Minor == 3) return "Windows 8.1 / Server 2012 R2";
            if (v.Major >= 10) return FriendlyModernWindows();
        }
        catch
        {
            // fall through
        }

        return "Windows";
    }

    private static string FriendlyModernWindows()
    {
        // Environment.OSVersion is often 6.2 under appcompat; try RtlGetVersion.
        try
        {
            var info = new OsVersionInfo { OSVersionInfoSize = Marshal.SizeOf(typeof(OsVersionInfo)) };
            if (RtlGetVersion(ref info) == 0)
            {
                if (info.MajorVersion == 10)
                {
                    if (info.BuildNumber >= 22000) return "Windows 11 (build " + info.BuildNumber + ")";
                    return "Windows 10 (build " + info.BuildNumber + ")";
                }

                if (info.MajorVersion == 6 && info.MinorVersion == 3) return "Windows 8.1";
                if (info.MajorVersion == 6 && info.MinorVersion == 2) return "Windows 8";
                if (info.MajorVersion == 6 && info.MinorVersion == 1) return "Windows 7";
            }
        }
        catch
        {
            // ignore
        }

        return "Windows 8+ (reported as 6.2)";
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
