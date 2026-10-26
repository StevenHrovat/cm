using CM.BlackHole.Doppler;
using Im.Trust;
using Xunit;

namespace CM.BlackHole.Tests.Doppler;

public sealed class TokenTests : IDisposable
{
    readonly DopplerWorld world = new();

    public void Dispose() => world.Dispose();

    [Fact]
    public void An_enrolled_machine_gets_a_token_for_its_own_key_role_and_the_v0_permissions()
    {
        var (agent, _, _) = world.NewMachine(role: EnrolledRole.Test);
        agent.Start();
        agent.Enrol();
        Assert.True(agent.RefreshToken());

        var claims = Assert.Single(world.Encoder.Issued);
        Assert.Equal(agent.Subject, claims.Subject);
        Assert.Equal(EnrolledRole.Test, claims.Role);
        Assert.Equal(agent.DeviceSigner!.KeyId, claims.DeviceKeyId);
        Assert.Equal(["im.session.open", "im.heartbeat.send"], claims.Permissions);
        Assert.Equal(world.Clock.GetUtcNow() + world.Settings.TokenLifetime, claims.Expires);
        Assert.Equal(claims.Expires, agent.Token!.Expires);
    }

    [Fact]
    public void A_token_request_for_an_unknown_subject_or_with_a_wrong_proof_is_refused()
    {
        var (agent, _, _) = world.NewMachine();
        agent.Start();
        agent.Enrol();

        var challenge = world.Server.Challenge()!;
        Assert.Equal(DopplerRefusal.NotEnrolled, world.Server.IssueToken(new TokenRequest("dev-unknown", challenge, new byte[64])).Refusal);

        var other = world.NewTpm();
        other.CreateDeviceKey();
        var again = world.Server.Challenge()!;
        var forged = other.SignWithDeviceKey(EnrolmentStatements.TokenProof(agent.Subject!, again));
        Assert.Equal(DopplerRefusal.BadProof, world.Server.IssueToken(new TokenRequest(agent.Subject!, again, forged)).Refusal);
        Assert.Empty(world.Encoder.Issued);
    }

    [Fact]
    public void A_revoked_machine_gets_no_token_and_its_certificate_is_on_the_CRL()
    {
        var (agent, _, _) = world.NewMachine();
        agent.Start();
        agent.Enrol();
        Assert.True(world.Server.Revoke(agent.Subject!));

        Assert.False(agent.RefreshToken());
        Assert.Equal(DopplerRefusal.Revoked, agent.LastRefusal);
        Assert.True(world.Authority.IsRevoked(agent.DeviceSigner!.Certificate));
    }

    [Fact]
    public void The_audit_names_actions_and_subjects_and_never_a_key_or_a_token()
    {
        var (agent, _, _) = world.NewMachine();
        agent.Start();
        agent.Enrol();
        agent.RefreshToken();

        var secrets = new[] { agent.DeviceSigner!.KeyId, world.TokenSigner.KeyId, Convert.ToBase64String(agent.Token!.Encoded), Convert.ToHexStringLower(agent.Token.Encoded) };
        Assert.Contains(world.Audit.Lines, l => l.Action == "enrol" && l.Target == agent.Subject);
        Assert.Contains(world.Audit.Lines, l => l.Action == "token.issue" && l.Target == agent.Subject);
        foreach (var line in world.Audit.Lines)
            foreach (var secret in secrets)
                Assert.DoesNotContain(secret, line.Action + line.Target + line.Outcome, StringComparison.Ordinal);
    }
}
