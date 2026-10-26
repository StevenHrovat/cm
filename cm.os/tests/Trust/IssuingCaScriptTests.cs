using System.Text.RegularExpressions;
using Im.Trust;
using Xunit;

namespace cm.os.Tests.Trust;

/// <summary>The issuing CA's own certificate limits every chain to the CMOS roles (tools/blackhole/issuing-ca.ps1, CAPolicy.inf). The script and the code must name the same OIDs.</summary>
public sealed class IssuingCaScriptTests
{
    static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CM.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CM.slnx not found above the test binaries");
    }

    [Fact]
    public void The_script_limits_the_issuing_CA_to_exactly_the_role_OIDs_of_the_code()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "blackhole", "issuing-ca.ps1"));

        var arc = Regex.Match(script, @"\$CmArc = '([0-9.]+)'").Groups[1].Value;
        Assert.Equal(TrustOids.Arc, arc);

        var roles = Regex.Matches(script, @"""\$CmArc(\.[0-9.]+)""").Select(m => arc + m.Groups[1].Value).ToHashSet();
        var code = Enum.GetValues<TrustRole>().Select(TrustOids.Eku).ToHashSet();
        Assert.Equal(code.Order(), roles.Order());

        Assert.Contains($"$ServerAuth = '{TrustOids.ServerAuth}'", script, StringComparison.Ordinal);
        Assert.Contains($"$ClientAuth = '{TrustOids.ClientAuth}'", script, StringComparison.Ordinal);
        Assert.Contains("PathLength=0", script, StringComparison.Ordinal);
    }
}
