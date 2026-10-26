using CM.BlackHole.EventHorizon;
using cm.os.Inspection;
using Xunit;

namespace CM.BlackHole.Tests.EventHorizon;

/// <summary>
/// The refusal suite for the Event Horizon decision layer (Supernova level 3, CM.BlackHole/docs/event-horizon-inspection.md
/// section 10): every bypass reason has a test that produces it, and no path reaches content inspection that must not.
/// Deterministic: no clock, no machine name, synthetic data only.
/// </summary>
public sealed class EventHorizonPolicyTests
{
    static InspectionSettings On(params string[] contentEnabled) => new()
    {
        InspectionEnabled = true,
        MicrosoftManaged = SandboxConnections.MicrosoftManaged,
        ContentEnabledCategories = new HashSet<string>(contentEnabled, StringComparer.OrdinalIgnoreCase),
    };

    [Fact]
    public void Switch_off_bypasses_everything_even_a_content_enabled_host()
    {
        var policy = new EventHorizonPolicy(new InspectionSettings { InspectionEnabled = false });
        foreach (var c in SandboxConnections.All)
        {
            var v = policy.Decide(c);
            Assert.Equal(InspectionDepth.Bypass, v.Depth);
            Assert.Equal(InspectionReason.SwitchOff, v.Reason);
        }
    }

    [Fact]
    public void Microsoft_managed_is_bypassed_even_when_its_category_is_content_enabled()
    {
        // The host is tagged "general-web" and that category is content-enabled, but EH-BYPASS-1 wins (step 2 before step 5).
        var policy = new EventHorizonPolicy(On("general-web", "personal-webmail"));

        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.MicrosoftManaged),
            policy.Decide(new ConnectionFacts("mx1.protection.outlook.com", "general-web")));
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.MicrosoftManaged),
            policy.Decide(new ConnectionFacts("outlook.office.com", "personal-webmail")));
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.MicrosoftManaged),
            policy.Decide(new ConnectionFacts("device.manage.microsoft.com", null)));
    }

    [Fact]
    public void Uncategorised_is_metadata_only_never_content()
    {
        var policy = new EventHorizonPolicy(On());
        var v = policy.Decide(new ConnectionFacts("unknown.example.com", null));
        Assert.Equal(InspectionDepth.InspectMetadata, v.Depth);
        Assert.Equal(InspectionReason.PinnedUncategorised, v.Reason);
    }

    [Theory]
    [InlineData("banking-finance")]
    [InlineData("health-medical")]
    [InlineData("government-tax")]
    [InlineData("personal-webmail")]
    [InlineData("legal-counsel")]
    [InlineData("works-council")]
    public void Sensitive_category_is_bypassed_even_when_also_content_enabled(string category)
    {
        // Content-enable the sensitive category: the sensitive bypass (step 4) still wins over content (step 5).
        var policy = new EventHorizonPolicy(On(category));
        var v = policy.Decide(new ConnectionFacts("host.example.com", category));
        Assert.Equal(InspectionDepth.Bypass, v.Depth);
        Assert.Equal(InspectionReason.SensitiveCategory, v.Reason);
    }

    [Fact]
    public void Categorised_non_sensitive_not_content_enabled_is_metadata_only()
    {
        var policy = new EventHorizonPolicy(On());
        var v = policy.Decide(new ConnectionFacts("news.example.com", "general-web"));
        Assert.Equal(InspectionDepth.InspectMetadata, v.Depth);
        Assert.Equal(InspectionReason.MetadataOnly, v.Reason);
    }

    [Fact]
    public void Content_is_reachable_only_for_a_content_enabled_non_sensitive_category()
    {
        var policy = new EventHorizonPolicy(On("general-web"));
        var v = policy.Decide(new ConnectionFacts("news.example.com", "general-web"));
        Assert.Equal(InspectionVerdict.Content(InspectionReason.ContentPermitted), v);
    }

    [Fact]
    public void No_connection_in_the_sandbox_reaches_content_unless_switch_on_categorised_nonsensitive_and_enabled()
    {
        // Exhaustive adversarial sweep (GRB-style): try to reach content from every synthetic connection under every
        // single-category content-enable, with the switch both off and on. Content may appear ONLY under the exact
        // conjunction the design promises.
        var categories = new[]
        {
            "general-web", "software-update", "banking-finance", "health-medical",
            "government-tax", "personal-webmail", "legal-counsel", "works-council", InspectionCounters.UncategorisedKey,
        };

        foreach (var enabled in categories)
        foreach (var on in new[] { false, true })
        {
            var settings = new InspectionSettings
            {
                InspectionEnabled = on,
                MicrosoftManaged = SandboxConnections.MicrosoftManaged,
                ContentEnabledCategories = new HashSet<string>([enabled], StringComparer.OrdinalIgnoreCase),
            };
            var policy = new EventHorizonPolicy(settings);

            foreach (var c in SandboxConnections.All)
            {
                var v = policy.Decide(c);
                if (v.Depth != InspectionDepth.InspectContent) continue;

                Assert.True(on, "content with the switch off");
                Assert.False(string.IsNullOrWhiteSpace(c.Category), "content for an uncategorised host");
                Assert.False(SandboxConnections.MicrosoftManaged.Contains(c.DestinationHost), "content for a Microsoft-managed host");
                Assert.DoesNotContain(c.Category!, InspectionSettings.DefaultSensitiveCategories);
                Assert.Equal(enabled, c.Category);
                Assert.Equal(InspectionReason.ContentPermitted, v.Reason);
            }
        }
    }
}
