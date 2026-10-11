using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace MessagePurify.Windows.IntegrationProbe;

[SupportedOSPlatform("windows")]
internal static class ProcessIntegrity
{
    private const int TokenIntegrityLevel = 25;
    private const int InsufficientBuffer = 122;
    private const uint HighIntegrityRid = 0x3000;

    public static bool IsElevated() => CurrentRid() >= HighIntegrityRid;

    public static uint CurrentRid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        GetTokenInformation(identity.AccessToken, TokenIntegrityLevel, nint.Zero, 0, out var length);
        if (Marshal.GetLastWin32Error() != InsufficientBuffer) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(identity.AccessToken, TokenIntegrityLevel, buffer, length, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
            return uint.Parse(sid.Value.Split('-')[^1], CultureInfo.InvariantCulture);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        nint information, int informationLength, out int returnLength);
}
