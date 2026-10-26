namespace CM.BlackHole.Doppler;

/// <summary>
/// The writes an administrator makes to Doppler's state: add an approved plan, revoke a machine. The server uses the same methods,
/// and so does the admin command on the Doppler server (which runs while the server runs: the file store makes both see one state).
/// In a deployment the plan comes from the administration tool's plan and four-eyes gate.
/// </summary>
public static class DopplerAdmin
{
    /// <summary>Adds a plan when it has enough distinct approvers (two while four eyes is on). A plan for the same endorsement key replaces the older one.</summary>
    public static bool AddPlan(IDopplerStore store, DopplerSettings settings, EnrolmentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(plan);
        var approvers = plan.Approvers.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (approvers < (settings.FourEyes ? 2 : 1)) return false;
        if (string.IsNullOrWhiteSpace(plan.EndorsementKeyId) || plan.EndorsementKeyId.Length != 64 || !plan.EndorsementKeyId.All(Uri.IsHexDigit)) return false;
        var normalised = plan with { EndorsementKeyId = plan.EndorsementKeyId.ToLowerInvariant() };
        store.Update(s => s with { Plans = s.Plans.SetItem(normalised.EndorsementKeyId, normalised) });
        return true;
    }

    /// <summary>Marks a machine revoked in the register. False when the subject is unknown or already revoked.</summary>
    public static bool Revoke(IDopplerStore store, string subject, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);
        var done = false;
        store.Update(s =>
        {
            if (!s.Register.TryGetValue(subject, out var entry) || entry.Revoked) return s;
            done = true;
            return s with { Register = s.Register.SetItem(subject, entry with { Revoked = true, RevokedAt = now }) };
        });
        return done;
    }

    /// <summary>Withdraws a plan that has not been used yet. False when there is none for that endorsement key.</summary>
    public static bool RemovePlan(IDopplerStore store, string endorsementKeyId)
    {
        ArgumentNullException.ThrowIfNull(store);
        var done = false;
        store.Update(s =>
        {
            done = s.Plans.ContainsKey(endorsementKeyId);
            return done ? s with { Plans = s.Plans.Remove(endorsementKeyId) } : s;
        });
        return done;
    }
}
