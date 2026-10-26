using CM.BlackHole.EventHorizon;
using cm.os.Inspection;

namespace CM.BlackHole.Tests.EventHorizon;

/// <summary>
/// The synthetic world the Event Horizon tests and the sandbox GRB run drive the policy with. Every host is a made-up
/// placeholder (no real company value, C9); every category is one of the real category names. This is "the made-up company
/// of the sandbox" (defensive charter): no real destination, no real device, nothing leaves memory.
/// </summary>
public static class SandboxConnections
{
    /// <summary>A few real, public Microsoft-managed patterns as the M365 web service and the Intune page publish them (wildcards and apex). Public names, no company value (C9).</summary>
    public static DomainSet MicrosoftManaged { get; } = new(
    [
        "*.protection.outlook.com",
        "outlook.office.com",
        "*.manage.microsoft.com",
        "*.dm.microsoft.com",
        "*.wns.windows.com",
    ]);

    /// <summary>A spread of synthetic connections across every category and shape, for exhaustive and property checks.</summary>
    public static IReadOnlyList<ConnectionFacts> All { get; } =
    [
        // Microsoft-managed (deliberately given a content-enabled category too, to prove the bypass wins — see tests).
        new("mx1.protection.outlook.com", "general-web"),
        new("outlook.office.com", "personal-webmail"),
        new("device.manage.microsoft.com", null),
        // Sensitive categories.
        new("login.bank.example.com", "banking-finance"),
        new("portal.health.example.org", "health-medical"),
        new("tax.gov.example.net", "government-tax"),
        new("webmail.example.net", "personal-webmail"),
        new("counsel.example.com", "legal-counsel"),
        // Categorised, non-sensitive.
        new("news.example.com", "general-web"),
        new("cdn.example.org", "software-update"),
        // Uncategorised.
        new("unknown.example.com", null),
        new("", null),
    ];
}
