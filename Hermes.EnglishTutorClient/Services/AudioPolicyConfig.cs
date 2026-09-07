using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>Set Windows default audio endpoint (wake BT Hands-Free / HFP mic).</summary>
internal static class AudioPolicyConfig
{
    public static bool TrySetDefaultEndpoint(MMDevice device, Role role)
    {
        if (device == null) return false;
        var eRole = role switch
        {
            Role.Console => 0,
            Role.Multimedia => 1,
            _ => 2, // Communications
        };

        if (TryPolicyConfig(device.ID, eRole, out var hr1))
            return true;

        if (TryPolicyConfigVista(device.ID, eRole, out var hr2))
            return true;

        AppLog.Warn("PolicyConfig failed hr=0x" + hr1.ToString("X8")
            + " vista=0x" + hr2.ToString("X8")
            + " role=" + role);
        return false;
    }

    public static void TryClaimAllRoles(MMDevice device)
    {
        if (device == null) return;
        TrySetDefaultEndpoint(device, Role.Console);
        TrySetDefaultEndpoint(device, Role.Multimedia);
        TrySetDefaultEndpoint(device, Role.Communications);
    }

    private static bool TryPolicyConfig(string deviceId, int eRole, out int hr)
    {
        hr = unchecked((int)0x80004005);
        try
        {
            var client = (IPolicyConfig)new CPolicyConfigClient();
            hr = client.SetDefaultEndpoint(deviceId, eRole);
            return hr == 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn("PolicyConfig exception: " + ex.Message);
            return false;
        }
    }

    private static bool TryPolicyConfigVista(string deviceId, int eRole, out int hr)
    {
        hr = unchecked((int)0x80004005);
        try
        {
            var client = (IPolicyConfigVista)new CPolicyConfigVistaClient();
            hr = client.SetDefaultEndpoint(deviceId, eRole);
            return hr == 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn("PolicyConfigVista exception: " + ex.Message);
            return false;
        }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient
    {
    }

    [ComImport, Guid("294935CE-F637-4E7C-A41B-AB255460B862")]
    private class CPolicyConfigVistaClient
    {
    }

    /// <summary>Correct Win7+ IPolicyConfig vtable (wrong slot caused 0x800706F4).</summary>
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int eRole);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bVisible);
    }

    [Guid("568b9108-44bf-40b4-9006-86afe5b5a620"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
        [PreserveSig] int SetEndpointVisibility();
    }
}
