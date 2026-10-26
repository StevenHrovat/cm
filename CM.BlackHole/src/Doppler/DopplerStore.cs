using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Im.Trust;

namespace CM.BlackHole.Doppler;

/// <summary>
/// One machine in Doppler's register. <see cref="Certificate"/> is the DER of its Device certificate (public, no key);
/// <see cref="OnCrl"/> says whether a revocation has reached the issuing CA's list yet (a revocation written by the admin command
/// while the server runs is put on the list by the server's next <see cref="DopplerServer.SyncRevocations"/>).
/// </summary>
public sealed record RegisterEntry(
    string Subject,
    EnrolledRole Role,
    string KeyId,
    string EndorsementKeyId,
    byte[] Certificate,
    DateTimeOffset EnrolledAt,
    bool Revoked,
    DateTimeOffset? RevokedAt,
    bool OnCrl)
{
    public EnrolmentRecord Record => new(Subject, Role, KeyId, Revoked);
}

/// <summary>
/// Everything Doppler must keep across a restart: the approved plans (by endorsement key id, single use), the register (by subject)
/// and the ids of the tokens it issued with their expiry (for the unknown-token alarm). No key, no token, no secret: certificates
/// are public and token ids are random names.
/// </summary>
public sealed record DopplerState(
    ImmutableDictionary<string, EnrolmentPlan> Plans,
    ImmutableDictionary<string, RegisterEntry> Register,
    ImmutableDictionary<string, DateTimeOffset> IssuedTokens)
{
    public static DopplerState Empty { get; } = new(
        ImmutableDictionary.Create<string, EnrolmentPlan>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, RegisterEntry>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, DateTimeOffset>(StringComparer.Ordinal));
}

/// <summary>
/// Where Doppler's state lives (the register "lives with the Doppler server", lane L4b goal 1). Reads see the newest state, also one
/// written by another process (the admin command); an update is an atomic read-modify-write.
/// </summary>
public interface IDopplerStore
{
    DopplerState Read();

    DopplerState Update(Func<DopplerState, DopplerState> change);
}

/// <summary>The store of the sandbox and the unit tests: memory only.</summary>
public sealed class InMemoryDopplerStore : IDopplerStore
{
    DopplerState state = DopplerState.Empty;
    readonly Lock gate = new();

    public DopplerState Read() => Volatile.Read(ref state);

    public DopplerState Update(Func<DopplerState, DopplerState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            var next = change(state);
            Volatile.Write(ref state, next);
            return next;
        }
    }
}

/// <summary>
/// Doppler's state in one JSON file in a directory only Doppler's account and administrators may write (the installer sets the
/// folder's access list). Writers (the server and the admin command) take an exclusive lock file, re-read, change and replace the
/// file atomically; readers reload when the file changed, so a revocation written by the admin command is seen by the verifier on
/// the next message. The file holds no secret.
/// </summary>
public sealed class FileDopplerStore : IDopplerStore
{
    public const string FileName = "doppler-register.json";
    const string LockName = "doppler-register.lock";
    const int Version = 1;

    readonly string path;
    readonly string lockPath;
    readonly TimeSpan lockWait;
    readonly Lock gate = new();
    DopplerState cached = DopplerState.Empty;
    (DateTime Written, long Length) cachedStamp = (DateTime.MinValue, -1);

    public FileDopplerStore(string directory, TimeSpan? lockWait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, FileName);
        lockPath = Path.Combine(directory, LockName);
        this.lockWait = lockWait ?? TimeSpan.FromSeconds(10);
    }

    public DopplerState Read()
    {
        lock (gate)
        {
            var stamp = Stamp();
            if (stamp != cachedStamp)
            {
                cached = Load();
                cachedStamp = stamp;
            }
            return cached;
        }
    }

    public DopplerState Update(Func<DopplerState, DopplerState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            using var held = AcquireLock();
            var next = change(Load());
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, Serialise(next));
            File.Move(temp, path, overwrite: true);
            cached = next;
            cachedStamp = Stamp();
            return next;
        }
    }

    (DateTime, long) Stamp()
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1);
    }

    DopplerState Load()
    {
        if (!File.Exists(path)) return DopplerState.Empty;
        // A writer replaces the file atomically, so a read sees either the old or the new file, never half of one.
        var bytes = File.ReadAllBytes(path);
        return Deserialise(bytes);
    }

    FileStream AcquireLock()
    {
        // The lock waits with a bounded number of short tries; no wall clock is read, so the wait is the number of tries.
        var tries = Math.Max(1, (int)(lockWait.TotalMilliseconds / 50));
        for (var i = 0; ; i++)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < tries) { Thread.Sleep(50); }
        }
    }

    // ---- the file format ------------------------------------------------------------------------------------------

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    sealed record PlanDto(string PlanId, string EndorsementKeyId, string Role, string[] Approvers);

    sealed record EntryDto(string Subject, string Role, string KeyId, string EndorsementKeyId, string Certificate, string EnrolledAt, bool Revoked, string? RevokedAt, bool OnCrl);

    sealed record TokenDto(string Id, string Expires);

    sealed record FileDto(int Version, PlanDto[] Plans, EntryDto[] Register, TokenDto[] IssuedTokens);

    internal static byte[] Serialise(DopplerState state)
    {
        var dto = new FileDto(
            Version,
            state.Plans.Values.OrderBy(p => p.EndorsementKeyId, StringComparer.Ordinal)
                .Select(p => new PlanDto(p.PlanId, p.EndorsementKeyId, p.Role.ToString(), [.. p.Approvers])).ToArray(),
            state.Register.Values.OrderBy(e => e.Subject, StringComparer.Ordinal)
                .Select(e => new EntryDto(e.Subject, e.Role.ToString(), e.KeyId, e.EndorsementKeyId, Convert.ToBase64String(e.Certificate), Time(e.EnrolledAt), e.Revoked,
                    e.RevokedAt is { } r ? Time(r) : null, e.OnCrl)).ToArray(),
            state.IssuedTokens.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new TokenDto(t.Key, Time(t.Value))).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Json);
    }

    internal static DopplerState Deserialise(byte[] bytes)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(bytes, Json) ?? throw new InvalidDataException("the Doppler register file is empty");
        if (dto.Version != Version) throw new InvalidDataException("the Doppler register file has an unknown version");
        return new DopplerState(
            dto.Plans.ToImmutableDictionary(p => p.EndorsementKeyId, p => new EnrolmentPlan(p.PlanId, p.EndorsementKeyId, Enum.Parse<EnrolledRole>(p.Role), p.Approvers), StringComparer.Ordinal),
            dto.Register.ToImmutableDictionary(e => e.Subject, e => new RegisterEntry(e.Subject, Enum.Parse<EnrolledRole>(e.Role), e.KeyId, e.EndorsementKeyId,
                Convert.FromBase64String(e.Certificate), Parse(e.EnrolledAt), e.Revoked, e.RevokedAt is { } r ? Parse(r) : null, e.OnCrl), StringComparer.Ordinal),
            dto.IssuedTokens.ToImmutableDictionary(t => t.Id, t => Parse(t.Expires), StringComparer.Ordinal));
    }

    static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    static DateTimeOffset Parse(string t) => DateTimeOffset.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
