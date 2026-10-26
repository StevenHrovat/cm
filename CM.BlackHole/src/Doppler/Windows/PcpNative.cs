using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CM.BlackHole.Doppler.Tpm;

namespace CM.BlackHole.Doppler.Windows;

/// <summary>
/// The Windows calls the platform crypto provider path needs: CNG key storage (ncrypt.dll) and the TPM Base Services (tbs.dll).
/// Signatures follow the Windows SDK headers ncrypt.h and tbs.h; property names and the identity-key usage value are the ones the
/// provider documents ("Using the Windows 8 Platform Crypto Provider and Associated TPM Functionality", Microsoft TSS.MSR PCPTool)
/// and that Google's go-attestation uses in production (attest/pcp_windows.go).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PcpNative
{
    public const string ProviderName = "Microsoft Platform Crypto Provider";

    public const int NteBadKeyset = unchecked((int)0x80090016);
    public const int NteNotFound = unchecked((int)0x80090011);

    /// <summary>NCRYPT_PCP_IDENTITY_KEY: the key may only sign what the TPM produced (a restricted signing key).</summary>
    public const uint IdentityKeyUsage = 0x8;

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int NCryptOpenStorageProvider(out IntPtr provider, string providerName, uint flags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int NCryptOpenKey(IntPtr provider, out IntPtr key, string keyName, uint legacyKeySpec, uint flags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int NCryptCreatePersistedKey(IntPtr provider, out IntPtr key, string algorithm, string keyName, uint legacyKeySpec, uint flags);

    [DllImport("ncrypt.dll", ExactSpelling = true)]
    public static extern int NCryptFinalizeKey(IntPtr key, uint flags);

    /// <summary>Deletes the key from storage and frees the handle when it succeeds.</summary>
    [DllImport("ncrypt.dll", ExactSpelling = true)]
    public static extern int NCryptDeleteKey(IntPtr key, uint flags);

    [DllImport("ncrypt.dll", ExactSpelling = true)]
    public static extern int NCryptFreeObject(IntPtr handle);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int NCryptGetProperty(IntPtr handle, string property, byte[]? output, int outputSize, out int resultSize, uint flags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int NCryptSetProperty(IntPtr handle, string property, byte[] input, int inputSize, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct TbsContextParams2
    {
        public uint Version;
        public uint Flags;
    }

    /// <summary>TPM_VERSION_20 and includeTpm20 (bit 2) of TBS_CONTEXT_PARAMS2.</summary>
    public static TbsContextParams2 Tpm20 => new() { Version = 2, Flags = 1u << 2 };

    [DllImport("tbs.dll", ExactSpelling = true)]
    public static extern uint Tbsi_Context_Create(ref TbsContextParams2 parameters, out IntPtr context);

    [DllImport("tbs.dll", ExactSpelling = true)]
    public static extern uint Tbsip_Context_Close(IntPtr context);

    [DllImport("tbs.dll", ExactSpelling = true)]
    public static extern uint Tbsi_Get_TCG_Log(IntPtr context, byte[]? output, ref uint outputSize);

    [DllImport("tbs.dll", ExactSpelling = true)]
    public static extern uint Tbsip_Submit_Command(IntPtr context, uint locality, uint priority, byte[] command, uint commandSize, byte[] result, ref uint resultSize);

    public static byte[] GetProperty(IntPtr handle, string property)
    {
        Check(NCryptGetProperty(handle, property, null, 0, out var size, 0), property);
        var buffer = new byte[size];
        Check(NCryptGetProperty(handle, property, buffer, buffer.Length, out size, 0), property);
        return buffer.AsSpan(0, size).ToArray();
    }

    public static void SetProperty(IntPtr handle, string property, byte[] value) =>
        Check(NCryptSetProperty(handle, property, value, value.Length, 0), property);

    public static void Check(int status, string what)
    {
        if (status != 0) throw new PcpException(status, what);
    }

    /// <summary>The firmware's TCG event log of this boot, from TBS (Tbsi_Get_TCG_Log).</summary>
    public static byte[] TcgLog()
    {
        var parameters = Tpm20;
        var rc = Tbsi_Context_Create(ref parameters, out var context);
        if (rc != 0) throw new PcpException(unchecked((int)rc), "Tbsi_Context_Create");
        try
        {
            uint size = 0;
            Tbsi_Get_TCG_Log(context, null, ref size);
            var log = new byte[size];
            rc = Tbsi_Get_TCG_Log(context, log, ref size);
            if (rc != 0) throw new PcpException(unchecked((int)rc), "Tbsi_Get_TCG_Log");
            return log.AsSpan(0, (int)size).ToArray();
        }
        finally { Tbsip_Context_Close(context); }
    }
}

/// <summary>A CNG or TBS call failed. <see cref="Status"/> is the SECURITY_STATUS or TBS_RESULT; <see cref="Call"/> names the call or property.</summary>
public sealed class PcpException(int status, string call) : Exception($"{call} failed with 0x{status:X8}")
{
    public int Status { get; } = status;

    public string Call { get; } = call;
}

/// <summary>TPM commands through the TBS context the platform crypto provider owns, so the provider's key handles are valid in it.</summary>
[SupportedOSPlatform("windows")]
internal sealed class TbsTransport(IntPtr context) : ITpmTransport
{
    const uint LocalityZero = 0;
    const uint PriorityNormal = 200;

    public byte[] Submit(byte[] command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = new byte[4096];
        var size = (uint)result.Length;
        var rc = PcpNative.Tbsip_Submit_Command(context, LocalityZero, PriorityNormal, command, (uint)command.Length, result, ref size);
        if (rc != 0) throw new PcpException(unchecked((int)rc), "Tbsip_Submit_Command");
        return result.AsSpan(0, (int)size).ToArray();
    }
}
