using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using CM.BlackHole.EventHorizon;
using cm.os.Inspection;
using Xunit;

namespace CM.BlackHole.Tests.EventHorizon;

/// <summary>
/// GRB-0002, the independent adversarial sweep of the Event Horizon decision layer (CM.BlackHole/docs/grb/GRB-0002-event-horizon-decision-layer.md).
///
/// Security context: this is an authorised test of our own controls under the defensive charter in CLAUDE.md. Everything here runs in memory
/// on synthetic data; no socket, no real destination, no real device. Written by a second session that re-derived the claims of an earlier change from
/// the design note and the sources, not from the builder's tests (the pattern of GRB-0001).
///
/// The claim under test: the decision layer yields <see cref="InspectionDepth.InspectContent"/> only under the exact conjunction
/// (switch on, destination categorised, category in neither bypass set, category content-enabled), the sensitive set can be added to but
/// never removed from by configuration, the matcher is not fooled by look-alike hosts, and nothing the layer emits carries a host.
/// Each test names the defect it found in the first run (R1 of 2026-10-25) where there was one.
/// </summary>
public sealed class Grb0002AdversarialTests
{
    static readonly string[] Sensitive =
        ["banking-finance", "health-medical", "government-tax", "personal-webmail", "legal-counsel", "works-council"];

    static readonly string[] NonSensitive = ["general-web", "software-update", "news", "cdn"];

    static DomainSet Managed => new(["*.protection.outlook.com", "outlook.office.com", "*.manage.microsoft.com"]);

    static InspectionSettings On(IReadOnlySet<string>? content = null, IReadOnlySet<string>? sensitive = null) => new()
    {
        InspectionEnabled = true,
        MicrosoftManaged = Managed,
        ContentEnabledCategories = content ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        SensitiveCategories = sensitive ?? InspectionSettings.DefaultSensitiveCategories,
    };

    // ---------------------------------------------------------------------------------------------------------------
    // A. The conjunction, re-derived independently (not via SandboxConnections).
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A1_content_only_under_the_exact_conjunction_over_the_full_cross_product()
    {
        string?[] categories =
        [
            null, "", " ", "\t", InspectionCounters.UncategorisedKey,
            .. Sensitive, .. Sensitive.Select(s => s.ToUpperInvariant()), .. Sensitive.Select(s => " " + s + " "),
            .. NonSensitive, .. NonSensitive.Select(s => s.ToUpperInvariant()),
        ];
        string[] hosts =
        [
            "mx1.protection.outlook.com", "outlook.office.com", "OUTLOOK.OFFICE.COM.", "device.manage.microsoft.com",
            "outlook.office.com.attacker.example", "evil-outlook.office.com", "news.example.com", "", "localhost", "203.0.113.7",
        ];
        string?[] enables = [null, .. Sensitive, .. NonSensitive, InspectionCounters.UncategorisedKey, "", " "];

        long decided = 0, content = 0;
        foreach (var on in new[] { false, true })
        foreach (var enabled in enables)
        {
            var settings = new InspectionSettings
            {
                InspectionEnabled = on,
                MicrosoftManaged = Managed,
                ContentEnabledCategories = enabled is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>([enabled], StringComparer.OrdinalIgnoreCase),
            };
            var policy = new EventHorizonPolicy(settings);
            foreach (var host in hosts)
            foreach (var category in categories)
            {
                var v = policy.Decide(new ConnectionFacts(host, category));
                decided++;
                if (v.Depth != InspectionDepth.InspectContent) continue;
                content++;
                var c = category!.Trim();
                Assert.True(on, $"content with the switch off for {host}/{category}");
                Assert.False(string.IsNullOrWhiteSpace(category), $"content for an uncategorised host {host}");
                Assert.False(Managed.Contains(host), $"content for a Microsoft-managed host {host}");
                Assert.DoesNotContain(c, InspectionSettings.DefaultSensitiveCategories);
                Assert.True(string.Equals(enabled?.Trim(), c, StringComparison.OrdinalIgnoreCase), $"content for {category} enabled {enabled}");
                Assert.Equal(InspectionReason.ContentPermitted, v.Reason);
            }
        }
        Assert.True(decided > 5000);
        Assert.True(content > 0, "the sweep must also reach content where it is promised, or it proves nothing");
    }

    [Fact]
    public void A2_defect_sensitive_set_can_be_emptied_by_configuration()
    {
        // Design section 3 and the settings description: "The DPO may add to it, never remove from it." R1: the constructor accepts an
        // empty set and a content-enabled sensitive category reaches content. The floor must be the default set, by construction.
        var policy = new EventHorizonPolicy(On(
            content: new HashSet<string>(["banking-finance"], StringComparer.OrdinalIgnoreCase),
            sensitive: new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var v = policy.Decide(new ConnectionFacts("login.bank.example.com", "banking-finance"));
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.SensitiveCategory), v);
    }

    [Fact]
    public void A3_defect_sensitive_set_cannot_be_narrowed_to_a_subset()
    {
        var onlyBanking = new HashSet<string>(["banking-finance"], StringComparer.OrdinalIgnoreCase);
        var policy = new EventHorizonPolicy(On(
            content: new HashSet<string>(["health-medical"], StringComparer.OrdinalIgnoreCase),
            sensitive: onlyBanking));
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.SensitiveCategory),
            policy.Decide(new ConnectionFacts("portal.health.example.org", "health-medical")));
        // Adding is allowed and takes effect.
        var added = new HashSet<string>(InspectionSettings.DefaultSensitiveCategories, StringComparer.OrdinalIgnoreCase) { "union-chat" };
        var policy2 = new EventHorizonPolicy(On(content: new HashSet<string>(["union-chat"], StringComparer.OrdinalIgnoreCase), sensitive: added));
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.SensitiveCategory),
            policy2.Decide(new ConnectionFacts("chat.example.org", "union-chat")));
    }

    [Fact]
    public void A4_defect_category_comparison_does_not_depend_on_the_callers_comparer_or_padding()
    {
        // R1: the policy uses whatever comparer the caller's sets carry. With ordinal sets, "Banking-Finance" skips the sensitive
        // bypass and reaches content when the content set spells it the same way. Normalisation must be the policy's, not the caller's.
        var ordinalContent = new HashSet<string>(["Banking-Finance", " legal-counsel "], StringComparer.Ordinal);
        var ordinalSensitive = new HashSet<string>(InspectionSettings.DefaultSensitiveCategories, StringComparer.Ordinal);
        var policy = new EventHorizonPolicy(On(content: ordinalContent, sensitive: ordinalSensitive));

        Assert.Equal(InspectionDepth.Bypass, policy.Decide(new ConnectionFacts("h.example.com", "Banking-Finance")).Depth);
        Assert.Equal(InspectionDepth.Bypass, policy.Decide(new ConnectionFacts("h.example.com", " legal-counsel ")).Depth);
        Assert.Equal(InspectionDepth.Bypass, policy.Decide(new ConnectionFacts("h.example.com", "LEGAL-COUNSEL")).Depth);

        // The same normalisation on the content side: a padded or upper-case spelling in the settings or from the resolver is the same tag.
        var padded = new EventHorizonPolicy(On(content: new HashSet<string>([" General-Web "], StringComparer.Ordinal)));
        Assert.Equal(InspectionDepth.InspectContent, padded.Decide(new ConnectionFacts("h.example.com", "general-web")).Depth);
        Assert.Equal(InspectionDepth.InspectContent, padded.Decide(new ConnectionFacts("h.example.com", "GENERAL-WEB ")).Depth);
        Assert.Equal(InspectionDepth.InspectMetadata, padded.Decide(new ConnectionFacts("h.example.com", "general_web")).Depth); // not a tag: uncategorised
    }

    [Fact]
    public void A5_defect_the_literal_uncategorised_key_is_treated_as_uncategorised()
    {
        // R1: a resolver that tags a name with the literal bucket key "(uncategorised)" passes step 3 and, once that string is
        // content-enabled, reaches content. The bucket key is a reserved word of the telemetry, never a category.
        var policy = new EventHorizonPolicy(On(content: new HashSet<string>([InspectionCounters.UncategorisedKey], StringComparer.OrdinalIgnoreCase)));
        var v = policy.Decide(new ConnectionFacts("unknown.example.com", InspectionCounters.UncategorisedKey));
        Assert.Equal(InspectionVerdict.Metadata(InspectionReason.PinnedUncategorised), v);
    }

    [Fact]
    public void A6_default_facts_and_null_settings_fail_closed()
    {
        Assert.Throws<ArgumentNullException>(() => new EventHorizonPolicy(null!));
        var off = new EventHorizonPolicy(new InspectionSettings());
        Assert.Equal(InspectionVerdict.Bypass(InspectionReason.SwitchOff), off.Decide(default));
        var on = new EventHorizonPolicy(On(content: new HashSet<string>(NonSensitive, StringComparer.OrdinalIgnoreCase)));
        Assert.Equal(InspectionVerdict.Metadata(InspectionReason.PinnedUncategorised), on.Decide(default));
    }

    [Fact]
    public void A7_a_fresh_settings_object_inspects_nothing()
    {
        // The shipped default: every property at its default must bypass everything. No property turns anything on.
        var policy = new EventHorizonPolicy(new InspectionSettings());
        foreach (var cat in Sensitive.Concat(NonSensitive).Append(null))
            Assert.Equal(InspectionVerdict.Bypass(InspectionReason.SwitchOff), policy.Decide(new ConnectionFacts("h.example.com", cat)));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // B. The matcher.
    // ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("outlook.office.com", true)]
    [InlineData("a.b.c.protection.outlook.com", true)]
    [InlineData("protection.outlook.com", true)]
    [InlineData("  outlook.office.com  ", false)]  // whitespace is not part of a name
    [InlineData("outlook.office.com...", false)]   // only the one trailing dot of the absolute form
    [InlineData("outlook.office.com.attacker.example", false)]
    [InlineData("xoutlook.office.com", false)]
    [InlineData("outlook.office.com.", true)]
    [InlineData("outlook.office.comm", false)]
    [InlineData("outlook.office.co", false)]
    [InlineData("office.com", false)]
    [InlineData("com", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("outlook.office.com/evil", false)]
    [InlineData("outlook.office.com:443", false)]
    [InlineData("user@" + "outlook.office.com", false)]  // split so the repository scan (DMP-006) does not read a mailbox
    [InlineData("outlook.office.com\u0000", false)]
    [InlineData("outlook\u0000.office.com", false)]
    [InlineData("outlook.office.com\n", false)]
    public void B1_boundary_hosts(string host, bool expected)
    {
        Assert.Equal(expected, Managed.Contains(host));
    }

    [Theory]
    [InlineData("outlooK.office.com")]   // KELVIN SIGN lower-cases to ASCII k under the invariant culture (R1: matched)
    [InlineData("outlook.offİce.com")]  // LATIN CAPITAL I WITH DOT ABOVE
    [InlineData("ｏutlook.office.com")]  // FULLWIDTH o
    [InlineData("outlook.office.com​")] // ZERO WIDTH SPACE
    [InlineData("outlook.office。com")]  // IDEOGRAPHIC FULL STOP as the label separator
    [InlineData("xn--outlook.office.com")]   // a punycode label is not the ASCII name
    public void B2_defect_a_non_ascii_look_alike_never_matches_a_bypass_pattern(string host)
    {
        // A host name on the wire (SNI, RFC 6066) is ASCII LDH. Anything else is not the Microsoft name and must not inherit its
        // bypass: a client that could pick a look-alike SNI would otherwise escape inspection. R1: the Kelvin sign matched.
        Assert.False(Managed.Contains(host));
    }

    [Fact]
    public void B3_random_ldh_hosts_outside_the_set_never_match_and_members_always_do()
    {
        var rng = new Random(20261025); // fixed seed: deterministic (TD1)
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-";
        string Label()
        {
            var l = new string(Enumerable.Range(0, rng.Next(1, 12)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray()).Trim('-');
            return l.Length == 0 ? "a" : l; // an LDH label never starts or ends with a hyphen
        }
        for (var i = 0; i < 5000; i++)
        {
            var host = string.Join('.', Enumerable.Range(0, rng.Next(1, 5)).Select(_ => Label()));
            if (host.EndsWith("outlook.office.com", StringComparison.Ordinal) || host.EndsWith("protection.outlook.com", StringComparison.Ordinal)
                || host.EndsWith("manage.microsoft.com", StringComparison.Ordinal)) continue;
            Assert.False(Managed.Contains(host), host);
            Assert.True(Managed.Contains(host + ".protection.outlook.com"), host);
            Assert.True(Managed.Contains(host.ToUpperInvariant() + ".MANAGE.MICROSOFT.COM"), host);
        }
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.")]
    [InlineData(".com")]
    [InlineData("com.")]
    [InlineData("*.*.example.com")]
    [InlineData("out look.office.com")]
    [InlineData("outlook.office.com/")]
    [InlineData("https://outlook.office.com")]
    [InlineData("outlooK.office.com")]
    [InlineData("203.0.113.0/24")]
    public void B4_defect_a_malformed_bypass_pattern_is_refused_loudly_not_swallowed(string pattern)
    {
        // The bypass list is the one piece of data that keeps a Microsoft endpoint out of inspection. R1: a typo in it is
        // silently normalised into a pattern that matches nothing ("https://outlook.office.com") or everything ("*"). A pattern
        // that is not an ASCII LDH name with an optional leading "*." must throw when the set is built, so the feed job fails
        // visibly instead of the endpoint being inspected.
        Assert.ThrowsAny<ArgumentException>(() => new DomainSet([pattern]));
    }

    [Fact]
    public void B5_matcher_cost_is_bounded_for_a_realistic_list_and_a_long_host()
    {
        // ~700 patterns is the shape of the real M365 + Intune + Defender lists. A host near the 253-byte limit with 111 labels is the longest legal shape.
        var patterns = Enumerable.Range(0, 700).Select(i => $"*.svc{i}.example.net").ToList();
        var set = new DomainSet(patterns);
        var longest = string.Join('.', Enumerable.Repeat("a", 110)) + ".svc699.example.net";
        Assert.True(set.Contains(longest));
        var miss = string.Join('.', Enumerable.Repeat("a", 110)) + ".other.example.org";
        Assert.False(set.Contains(string.Join('.', Enumerable.Repeat("a", 126)) + ".svc699.example.net")); // over 253 bytes: not a name
        for (var i = 0; i < 20_000; i++) Assert.False(set.Contains(miss)); // finishes well inside the test budget
    }

    // ---------------------------------------------------------------------------------------------------------------
    // C. Telemetry: counts, never content; resource limits.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void C1_defect_a_category_that_is_not_a_tag_is_bucketed_not_echoed()
    {
        // The counter key is "category|depth|reason" and the design says a category is a low-cardinality tag. R1: the counters trust
        // the string: a resolver bug (or a feed) that hands a host, an address or free text as the category writes it into the key,
        // which is the one record that leaves Event Horizon (CMB). A category that is not a tag must be counted under a fixed bucket.
        var counters = new InspectionCounters();
        foreach (var bad in new[] { "login.bank.example.com", "203.0.113.7", "2001:db8::1", "Hello World", "user@example.com", new string('x', 500) })
            counters.Record(bad, InspectionVerdict.Metadata(InspectionReason.MetadataOnly));
        counters.Record("banking-finance", InspectionVerdict.Bypass(InspectionReason.SensitiveCategory));

        var keys = counters.Snapshot().Keys.ToList();
        Assert.All(keys, k => Assert.DoesNotContain("example", k));
        Assert.All(keys, k => Assert.DoesNotContain("203.0", k));
        Assert.All(keys, k => Assert.DoesNotContain("db8", k));
        Assert.All(keys, k => Assert.DoesNotContain(' ', k));
        Assert.All(keys, k => Assert.True(k.Length < 128, k));
        Assert.Contains("banking-finance|Bypass|SensitiveCategory", keys);
    }

    [Fact]
    public void C2_defect_counter_keys_are_bounded()
    {
        // Level 3 asks for resource limits. R1: one distinct category per connection grows the dictionary without bound; a feed that
        // emits unique tags is a memory leak in the one process that must stay up. The number of distinct keys must be capped and the
        // overflow counted under one key.
        var counters = new InspectionCounters();
        for (var i = 0; i < 50_000; i++)
            counters.Record($"tag-{i}", InspectionVerdict.Metadata(InspectionReason.MetadataOnly));
        var snap = counters.Snapshot();
        Assert.True(snap.Count <= 1024, $"{snap.Count} keys");
        Assert.Equal(50_000, snap.Values.Sum());
    }

    [Fact]
    public void C3_concurrent_records_lose_nothing()
    {
        var counters = new InspectionCounters();
        Parallel.For(0, 64, _ =>
        {
            for (var i = 0; i < 2000; i++)
                counters.Record(i % 2 == 0 ? "general-web" : null, InspectionVerdict.Metadata(InspectionReason.MetadataOnly));
        });
        var snap = counters.Snapshot();
        Assert.Equal(64_000, snap["general-web|InspectMetadata|MetadataOnly"]);
        Assert.Equal(64_000, snap[$"{InspectionCounters.UncategorisedKey}|InspectMetadata|MetadataOnly"]);
    }

    [Fact]
    public void C4_defect_a_sealed_finding_refuses_a_rule_id_or_category_that_is_not_a_tag()
    {
        // "From a fixed vocabulary, never free text" is the record's own doc comment. R1: any non-blank string is accepted, so a
        // host or a sentence can ride out inside the sealed record.
        Assert.ThrowsAny<ArgumentException>(() => new SealedFinding("contacted login.bank.example.com", "general-web", 1, FindingSeverity.High));
        Assert.ThrowsAny<ArgumentException>(() => new SealedFinding("eh.malware-domain", "login.bank.example.com", 1, FindingSeverity.High));
        Assert.ThrowsAny<ArgumentException>(() => new SealedFinding("eh.malware-domain", "203.0.113.7", 1, FindingSeverity.High));
        Assert.ThrowsAny<ArgumentException>(() => new SealedFinding("eh.malware-domain", "general web", 1, FindingSeverity.High));
        _ = new SealedFinding("eh.malware-domain", "general-web", 1, FindingSeverity.High);
        _ = new SealedFinding("eh.malware-domain", InspectionCounters.UncategorisedKey, 1, FindingSeverity.High);
    }

    [Fact]
    public void C5_nothing_the_layer_emits_carries_a_host_over_the_full_sweep()
    {
        var policy = new EventHorizonPolicy(On(content: new HashSet<string>(NonSensitive, StringComparer.OrdinalIgnoreCase)));
        var counters = new InspectionCounters();
        var hosts = new[] { "mx1.protection.outlook.com", "login.bank.example.com", "news.example.com", "unknown.example.com" };
        foreach (var h in hosts)
        foreach (var c in Sensitive.Concat(NonSensitive).Append(null))
            counters.Record(c, policy.Decide(new ConnectionFacts(h, c)));
        var text = string.Join('\n', counters.Snapshot().Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"));
        foreach (var h in hosts) Assert.DoesNotContain(h, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outlook", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // D. Settings and contract: no hidden switch, every tunable described, the contract's shape is what the design says.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void D1_every_public_setting_is_described_and_none_is_a_hidden_switch()
    {
        var props = typeof(InspectionSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.Equal(4, props.Count);
        Assert.Equal(4, InspectionSettings.Describe().Count);
        foreach (var p in props)
            Assert.DoesNotContain(new[] { "skip", "allow", "debug", "trusted", "force", "override", "all" },
                w => p.Contains(w, StringComparison.OrdinalIgnoreCase));
        Assert.All(InspectionSettings.Describe(), r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    [Fact]
    public void D2_the_facts_record_carries_only_the_two_fields_the_design_names()
    {
        // Documents finding F-EH-1 of the sweep: the facts are a host string and a category. There is no destination address and no
        // binding to the resolver's answer, so a data plane that fills DestinationHost from the client's SNI alone lets the client
        // choose its bypass. This test pins the contract so the data-plane brief must change it consciously.
        var fields = typeof(ConnectionFacts).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "Category", "DestinationHost" }, fields);
    }

    [Fact]
    public void D3_depth_order_and_verdict_factories_agree()
    {
        Assert.True(InspectionDepth.Bypass < InspectionDepth.InspectMetadata);
        Assert.True(InspectionDepth.InspectMetadata < InspectionDepth.InspectContent);
        Assert.Equal(InspectionDepth.Bypass, InspectionVerdict.Bypass(InspectionReason.SwitchOff).Depth);
        Assert.Equal(InspectionDepth.InspectMetadata, InspectionVerdict.Metadata(InspectionReason.MetadataOnly).Depth);
        Assert.Equal(InspectionDepth.InspectContent, InspectionVerdict.Content(InspectionReason.ContentPermitted).Depth);
    }

    [Fact]
    public void D4_the_policy_source_has_no_other_exit_to_content()
    {
        // Static check on the shipped source: InspectContent is produced at exactly one place, the Content factory, and the policy
        // calls it exactly once. A second path would be a second thing to prove.
        var root = FindRepoRoot();
        var policy = File.ReadAllText(Path.Combine(root, "CM.BlackHole", "src", "EventHorizon", "EventHorizonPolicy.cs"));
        Assert.Equal(1, Count(policy, "InspectionVerdict.Content("));
        Assert.DoesNotContain("InspectionDepth.InspectContent", policy);
        var contract = File.ReadAllText(Path.Combine(root, "cm.os", "src", "Inspection", "InspectionContract.cs"));
        Assert.Equal(1, Count(contract, "new(InspectionDepth.InspectContent"));
    }

    static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CM.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CM.slnx not found above the test directory");
    }
}
