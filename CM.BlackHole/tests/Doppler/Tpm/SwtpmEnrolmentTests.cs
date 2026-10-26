using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CM.BlackHole.Doppler;
using CM.BlackHole.Doppler.Simulation;
using CM.BlackHole.Doppler.Tpm;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler.Tpm;

/// <summary>
/// The agent's TPM commands against a real TPM 2.0 implementation (swtpm on libtpms), end to end through Doppler's checks (lane L4b
/// goal 2): the endorsement key, the attestation key, TPM2_ActivateCredential, a device key that never leaves the TPM, TPM2_Certify,
/// TPM2_Quote of PCR 7 and the event log the PCR was extended with. swtpm has no firmware, so each test "boots" it by extending PCR 7
/// with the events a UEFI firmware measures and keeps the matching log.
/// </summary>
public sealed class SwtpmEnrolmentTests : IDisposable
{
    readonly DopplerWorld world = new();
    readonly List<IDisposable> owned = [];

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(owned)) d.Dispose();
        world.Dispose();
    }

    static readonly ReportedState Good = new(DiskEncrypted: true, DopplerWorld.AgentPackage);

    (CommandTpm Tpm, Swtpm Swtpm) Machine(bool secureBoot = true, bool planned = true, Action<TpmCommands>? afterBoot = null)
    {
        var swtpm = new Swtpm();
        owned.Add(swtpm);
        var commands = new TpmCommands(swtpm.Transport);
        var log = Boot(commands, secureBoot);
        afterBoot?.Invoke(commands);
        var tpm = new CommandTpm(swtpm.Transport, () => log, () => Good);
        owned.Add(tpm);
        if (planned) Assert.True(world.Server.AddPlan(new EnrolmentPlan("plan-" + tpm.EndorsementKeyId[..8], tpm.EndorsementKeyId, EnrolledRole.Laptop, ["admin-a", "admin-b"])));
        return (tpm, swtpm);
    }

    /// <summary>What UEFI firmware measures into PCR 7 before it hands over: the Secure Boot state, PK, KEK, the separator, then the db entry that verified the boot loader.</summary>
    static byte[] Boot(TpmCommands tpm, bool secureBoot)
    {
        var global = SecureBootPolicy.EfiGlobalVariable;
        (uint Type, byte[] Data)[] events =
        [
            (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "SecureBoot", [secureBoot ? (byte)1 : (byte)0])),
            (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "PK", "test-pk"u8.ToArray())),
            (TcgEventType.EfiVariableDriverConfig, TcgLog.UefiVariable(global, "KEK", "test-kek"u8.ToArray())),
            (TcgEventType.Separator, new byte[4]),
            (TcgEventType.EfiVariableAuthority, TcgLog.UefiVariable(global, "db", "test-db-entry"u8.ToArray())),
        ];
        foreach (var (_, data) in events) tpm.PcrExtend(7, TpmAlg.Sha256, SHA256.HashData(data));
        return TcgLog.Build(events.Select(e => (7, e.Type, e.Data)));
    }

    DopplerAgent Agent(IDeviceTpm tpm)
    {
        var agent = new DopplerAgent(tpm, new SimulatedDeviceHost(), new DirectChannel(world.Server));
        Assert.Equal(AgentState.Started, agent.Start());
        return agent;
    }

    [SwtpmFact]
    public void A_machine_with_a_real_TPM_enrols_and_signs_with_a_device_key_that_lives_in_the_TPM()
    {
        var (tpm, _) = Machine();
        var agent = Agent(tpm);

        Assert.True(agent.Enrol(), agent.LastRefusal.ToString());

        Assert.Equal(AgentState.Enrolled, agent.State);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.on"]);
        var signer = agent.DeviceSigner!;
        Assert.Equal(world.Server.Registry.Find(agent.Subject!)!.KeyId, signer.KeyId);
        Assert.True(world.Authority.Anchors().Validate(signer.Certificate, signer.Chain, TrustRole.Device, world.Clock.GetUtcNow()).Ok);
        var signature = signer.Sign("heartbeat"u8);
        using var certified = signer.Certificate.GetECDsaPublicKey()!;
        Assert.True(certified.VerifyData("heartbeat"u8, signature, ImSignature.Hash, ImSignature.Format));
        Assert.True(agent.RefreshToken());

        var device = TpmPublic.Parse(TpmCommandsTestsHelper.DevicePublicOf(tpm));
        Assert.True(device.Has(TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin));
    }

    [SwtpmFact]
    public void Firmware_that_measured_Secure_Boot_off_is_refused()
    {
        var (tpm, _) = Machine(secureBoot: false);
        var agent = Agent(tpm);

        Assert.False(agent.Enrol());

        Assert.Equal(DopplerRefusal.PolicyFailed, agent.LastRefusal);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.off"]);
    }

    [SwtpmFact]
    public void A_PCR_7_extended_beyond_the_log_is_refused_as_a_replay_mismatch()
    {
        var (tpm, _) = Machine(afterBoot: t => t.PcrExtend(7, TpmAlg.Sha256, SHA256.HashData("not in the log"u8)));
        var agent = Agent(tpm);

        Assert.False(agent.Enrol());

        Assert.Equal(DopplerRefusal.PolicyFailed, agent.LastRefusal);
        Assert.Equal(1, world.Server.Counts["enrol.secureboot.replaymismatch"]);
    }

    [SwtpmFact]
    public void A_credential_sealed_to_one_TPM_does_not_open_in_another_nor_for_another_key()
    {
        var (planned, _) = Machine();
        var (other, _) = Machine(planned: false);

        var begun = world.Server.BeginEnrolment(new EnrolBegin(world.Server.Challenge()!, planned.EndorsementKeySpki, other.AttestationKeyPublic));
        Assert.True(begun.Activation is not null, begun.Refusal.ToString());

        Assert.Null(other.ActivateCredential(begun.Activation.CredentialBlob, begun.Activation.EncryptedSecret));
        Assert.Null(planned.ActivateCredential(begun.Activation.CredentialBlob, begun.Activation.EncryptedSecret));
    }

    [SwtpmFact]
    public void The_endorsement_key_and_the_device_key_are_found_again_after_a_restart()
    {
        var (first, swtpm) = Machine(planned: false);
        var devicePublic = first.CreateDeviceKey();
        var unique = first.DeviceKeyUnique!;
        var ekId = first.EndorsementKeyId;
        owned.Remove(first);
        first.Dispose();

        using var again = new CommandTpm(swtpm.Transport, () => [], () => Good, unique);

        Assert.Equal(ekId, again.EndorsementKeyId);
        Assert.Equal(devicePublic, TpmCommandsTestsHelper.DevicePublicOf(again));
    }

    [SwtpmFact]
    public void Dopplers_sealed_credential_opens_in_the_real_TPM()
    {
        var (tpm, _) = Machine(planned: false);
        using var ek = TpmEvidence.ImportEndorsementKey(tpm.EndorsementKeySpki)!;
        var secret = RandomNumberGenerator.GetBytes(32);
        var sealedCredential = TpmCredential.Make(ek, TpmPublic.Parse(tpm.AttestationKeyPublic).Name, secret);

        Assert.Equal(secret, tpm.ActivateCredential(sealedCredential.CredentialBlob, sealedCredential.EncryptedSecret) ?? throw new InvalidOperationException($"rc 0x{tpm.LastError:X}"));
    }
}

static class TpmCommandsTestsHelper
{
    /// <summary>The device key's public area as the TPM reports it (TPM2_Certify carries it; a certify over nothing reads it here).</summary>
    public static byte[] DevicePublicOf(CommandTpm tpm) => tpm.CertifyDeviceKey(new byte[32]).DeviceKeyPublic;
}
