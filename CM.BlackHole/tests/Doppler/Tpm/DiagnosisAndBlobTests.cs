using System.Buffers.Binary;
using System.Security.Cryptography;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Tpm;
using CM.BlackHole.Doppler.Windows;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Tpm;

/// <summary>The agent's diagnose command, and the platform crypto provider's blob formats (parsed without Windows).</summary>
public sealed class DiagnosisAndBlobTests : IDisposable
{
    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    [Fact]
    public void Diagnose_passes_on_the_software_TPM_and_prints_the_endorsement_key_id_and_no_secret()
    {
        var tpm = world.NewTpm();
        var lines = new List<string>();

        Assert.True(TpmDiagnosis.Run(tpm, lines.Add), string.Join("\n", lines));

        Assert.Contains(lines, l => l.Contains(tpm.EndorsementKeyId, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("FAIL", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnose_names_the_step_that_fails()
    {
        var tpm = world.NewTpm();
        tpm.ExtendedAfterBoot.Add((TcgEventType.EfiVariableDriverConfig,
            TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [0])));
        var lines = new List<string>();

        Assert.False(TpmDiagnosis.Run(tpm, lines.Add));

        Assert.Contains(lines, l => l.StartsWith("FAIL secure boot: Off", StringComparison.Ordinal));
    }

    [SwtpmFact]
    public void Diagnose_passes_on_a_real_TPM()
    {
        using var swtpm = new Swtpm();
        var commands = new TpmCommands(swtpm.Transport);
        var data = TcgLog.UefiVariable(SecureBootPolicy.EfiGlobalVariable, "SecureBoot", [1]);
        commands.PcrExtend(7, TpmAlg.Sha256, SHA256.HashData(data));
        commands.PcrExtend(7, TpmAlg.Sha256, SHA256.HashData(new byte[4]));
        var log = TcgLog.Build([(7, TcgEventType.EfiVariableDriverConfig, data), (7, TcgEventType.Separator, new byte[4])]);
        using var tpm = new CommandTpm(swtpm.Transport, () => log, () => new ReportedState(true, DopplerWorld.AgentPackage));
        var lines = new List<string>();

        Assert.True(TpmDiagnosis.Run(tpm, lines.Add), string.Join("\n", lines));
    }

    [Fact]
    public void The_endorsement_key_blob_of_the_provider_reads_as_the_same_public_key()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var blob = new byte[24 + p.Exponent!.Length + p.Modulus!.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, PcpBlobs.RsaPublicMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4), 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(8), (uint)p.Exponent.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(12), (uint)p.Modulus.Length);
        p.Exponent.CopyTo(blob, 24);
        p.Modulus.CopyTo(blob, 24 + p.Exponent.Length);

        Assert.Equal(rsa.ExportSubjectPublicKeyInfo(), PcpBlobs.SpkiFromRsaPublicBlob(blob));
        Assert.Throws<TpmFormatException>(() => PcpBlobs.SpkiFromRsaPublicBlob(blob.AsSpan(0, blob.Length - 1)));
        blob[0] ^= 1;
        Assert.Throws<TpmFormatException>(() => PcpBlobs.SpkiFromRsaPublicBlob(blob));
    }

    [Fact]
    public void The_identity_binding_of_a_TPM_2_0_yields_the_attestation_keys_public_area()
    {
        var tpm = world.NewTpm();
        var binding = new TpmWriter().Sized(tpm.AttestationKeyPublic).Sized([1, 2]).Sized([3]).Raw([0, 0x14, 0, 4]).ToArray();

        Assert.Equal(tpm.AttestationKeyPublic, PcpBlobs.PublicFromIdBinding(binding));
        Assert.Throws<TpmFormatException>(() => PcpBlobs.PublicFromIdBinding(new byte[] { 1, 1, 0, 0, 0, 0 }));
    }
}
