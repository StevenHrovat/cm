using CM.BlackHole.EventHorizon;
using cm.os.Inspection;
using Xunit;

namespace CM.BlackHole.Tests.EventHorizon;

public sealed class DomainSetTests
{
    [Theory]
    [InlineData("outlook.office.com", true)]              // exact apex
    [InlineData("OUTLOOK.OFFICE.COM", true)]              // case-insensitive
    [InlineData("outlook.office.com.", true)]             // trailing dot ignored
    [InlineData("mx.protection.outlook.com", true)]       // subdomain of *.protection.outlook.com
    [InlineData("protection.outlook.com", true)]          // apex of the wildcard matches too
    [InlineData("deep.mx.protection.outlook.com", true)]  // deeper subdomain
    [InlineData("office.com", false)]                     // parent is not a member
    [InlineData("notoutlook.office.com.evil.com", false)] // suffix trick: not a real subdomain
    [InlineData("evil-outlook.office.com", false)]        // ends with the suffix text but not at a label boundary
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_exact_subdomain_and_wildcard_only(string? host, bool expected)
    {
        var set = new DomainSet(["outlook.office.com", "*.protection.outlook.com"]);
        Assert.Equal(expected, set.Contains(host));
    }

    [Fact]
    public void A_suffix_trick_host_does_not_match()
    {
        var set = new DomainSet(["*.protection.outlook.com"]);
        Assert.False(set.Contains("protection.outlook.com.attacker.example"));
        Assert.False(set.Contains("xprotection.outlook.com")); // must be a label boundary
    }

    [Fact]
    public void Empty_set_matches_nothing()
    {
        Assert.False(DomainSet.Empty.Contains("anything.example.com"));
    }
}

public sealed class TelemetryTests
{
    [Fact]
    public void Counters_key_only_by_category_depth_reason_and_bucket_uncategorised()
    {
        var counters = new InspectionCounters();
        counters.Record("banking-finance", InspectionVerdict.Bypass(InspectionReason.SensitiveCategory));
        counters.Record("banking-finance", InspectionVerdict.Bypass(InspectionReason.SensitiveCategory));
        counters.Record(null, InspectionVerdict.Metadata(InspectionReason.PinnedUncategorised));

        var snap = counters.Snapshot();
        Assert.Equal(2, snap["banking-finance|Bypass|SensitiveCategory"]);
        Assert.Equal(1, snap[$"{InspectionCounters.UncategorisedKey}|InspectMetadata|PinnedUncategorised"]);
    }

    [Fact]
    public void No_counter_key_carries_a_host_or_an_address()
    {
        // Drive the policy over the whole sandbox and assert that no snapshot key contains a destination host.
        var policy = new EventHorizonPolicy(new InspectionSettings
        {
            InspectionEnabled = true,
            MicrosoftManaged = SandboxConnections.MicrosoftManaged,
            ContentEnabledCategories = new HashSet<string>(["general-web"], StringComparer.OrdinalIgnoreCase),
        });
        var counters = new InspectionCounters();
        foreach (var c in SandboxConnections.All)
            counters.Record(c.Category, policy.Decide(c));

        foreach (var key in counters.Snapshot().Keys)
        {
            foreach (var c in SandboxConnections.All)
                if (!string.IsNullOrEmpty(c.DestinationHost))
                    Assert.DoesNotContain(c.DestinationHost, key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.", key, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_sealed_finding_carries_no_host_or_content_and_validates_its_fields()
    {
        var finding = new SealedFinding("eh.malware-domain", "general-web", 3, FindingSeverity.High);
        Assert.Equal("eh.malware-domain", finding.RuleId);
        Assert.Equal(3, finding.Count);

        Assert.Throws<ArgumentException>(() => new SealedFinding("", "general-web", 1, FindingSeverity.Low));
        Assert.Throws<ArgumentException>(() => new SealedFinding("eh.x", " ", 1, FindingSeverity.Low));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SealedFinding("eh.x", "general-web", -1, FindingSeverity.Low));
    }

    [Fact]
    public void Settings_describe_every_tunable()
    {
        var keys = InspectionSettings.Describe().Select(r => r.Key).ToList();
        Assert.Contains("eventhorizon.inspection.enabled", keys);
        Assert.Contains("eventhorizon.bypass.microsoftManaged", keys);
        Assert.Contains("eventhorizon.bypass.sensitiveCategories", keys);
        Assert.Contains("eventhorizon.content.enabledCategories", keys);
    }
}
