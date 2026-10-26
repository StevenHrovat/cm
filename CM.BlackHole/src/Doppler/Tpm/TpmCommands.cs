using System.Buffers.Binary;

namespace CM.BlackHole.Doppler.Tpm;

/// <summary>
/// Moves one TPM 2.0 command to a TPM and its response back: the Windows TPM Base Services (TBS), a Linux resource manager
/// (<c>/dev/tpmrm0</c>) or, in tests, swtpm's TCP port. The bytes are the TPM 2.0 command and response exactly (Part 3), nothing added.
/// </summary>
public interface ITpmTransport
{
    byte[] Submit(byte[] command);
}

/// <summary>A TPM answered with a response code other than success. <see cref="ResponseCode"/> is the TPM_RC (TPM 2.0 Part 2, 6.6).</summary>
public sealed class TpmCommandException(uint responseCode, uint commandCode)
    : Exception($"TPM command 0x{commandCode:X3} answered 0x{responseCode:X}")
{
    public uint ResponseCode { get; } = responseCode;

    public uint CommandCode { get; } = commandCode;
}

/// <summary>The TPM 2.0 handles and codes the agent uses (TPM 2.0 Library Part 2).</summary>
public static class TpmHandle
{
    public const uint Owner = 0x40000001;
    public const uint Null = 0x40000007;
    public const uint Password = 0x40000009;
    public const uint Endorsement = 0x4000000B;
    public const uint Pcr7 = 0x00000007;
}

/// <summary>A key the TPM holds for this connection: its handle and its public area.</summary>
public sealed record TpmLoadedKey(uint Handle, byte[] Public)
{
    public TpmPublic Parsed => TpmPublic.Parse(Public);
}

/// <summary>
/// The few TPM 2.0 commands the agent needs, marshalled by hand (TPM 2.0 Library Part 3), so the same code runs over TBS on Windows
/// and over swtpm in CI. Every key authorisation is an empty password (TPM_RS_PW), the default of a fresh TPM and of the keys the
/// Windows platform crypto provider makes; the endorsement key is used through its standard policy (PolicySecret on the endorsement
/// hierarchy). Read only towards the machine: nothing here changes the TPM's hierarchies, NV or persistent handles.
/// </summary>
public sealed class TpmCommands(ITpmTransport transport)
{
    const ushort NoSessions = 0x8001;
    const ushort Sessions = 0x8002;

    const uint RcRetry = 0x922;
    const uint RcYielded = 0x908;
    const uint RcTesting = 0x90A;
    const int MaxTries = 16;

    const uint CcCreatePrimary = 0x131;
    const uint CcPolicySecret = 0x151;
    const uint CcActivateCredential = 0x147;
    const uint CcCertify = 0x148;
    const uint CcQuote = 0x158;
    const uint CcSign = 0x15D;
    const uint CcFlushContext = 0x165;
    const uint CcReadPublic = 0x173;
    const uint CcStartAuthSession = 0x176;
    const uint CcPcrRead = 0x17E;
    const uint CcPcrExtend = 0x182;

    /// <summary>The authPolicy of the TCG default endorsement key templates: PolicySecret(TPM_RH_ENDORSEMENT) with SHA-256.</summary>
    public static readonly byte[] EndorsementPolicy = Convert.FromHexString("837197674484B3F81A90CC8D46A5D724FD52D76E06520B64F2A1DA1B331469AA");

    // ---- templates ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The TCG EK Credential Profile's default RSA 2048 endorsement key template (template L-1): restricted decrypt, AES-128-CFB,
    /// admin with the endorsement policy, a 256-byte zero unique. Every TPM recreates the same key from it, so the agent reads the
    /// endorsement key without storing anything.
    /// </summary>
    public static byte[] EndorsementKeyTemplate() =>
        new TpmWriter().U16(TpmAlg.Rsa).U16(TpmAlg.Sha256)
            .U32((uint)(TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin
                | TpmObjectAttributes.AdminWithPolicy | TpmObjectAttributes.Restricted | TpmObjectAttributes.Decrypt))
            .Sized(EndorsementPolicy)
            .U16(TpmAlg.Aes).U16(128).U16(TpmAlg.Cfb)
            .U16(TpmAlg.Null).U16(2048).U32(0)
            .Sized(new byte[256])
            .ToArray();

    /// <summary>A restricted RSA 2048 signing key, RSASSA SHA-256, bound to the TPM: the attestation key, as a primary of the endorsement hierarchy.</summary>
    public static byte[] AttestationKeyTemplate() =>
        new TpmWriter().U16(TpmAlg.Rsa).U16(TpmAlg.Sha256)
            .U32((uint)(TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin
                | TpmObjectAttributes.UserWithAuth | TpmObjectAttributes.Restricted | TpmObjectAttributes.Sign))
            .Sized([])
            .U16(TpmAlg.Null).U16(TpmAlg.RsaSsa).U16(TpmAlg.Sha256).U16(2048).U32(0)
            .Sized([])
            .ToArray();

    /// <summary>
    /// An unrestricted ECC P-256 ECDSA SHA-256 signing key bound to the TPM, as a primary of the owner hierarchy. <paramref name="unique"/>
    /// picks the key: the same template with the same unique gives the same key again, so the agent keeps only the unique (not secret).
    /// </summary>
    public static byte[] DeviceKeyTemplate(byte[] unique)
    {
        ArgumentNullException.ThrowIfNull(unique);
        return new TpmWriter().U16(TpmAlg.Ecc).U16(TpmAlg.Sha256)
            .U32((uint)(TpmObjectAttributes.FixedTpm | TpmObjectAttributes.FixedParent | TpmObjectAttributes.SensitiveDataOrigin
                | TpmObjectAttributes.UserWithAuth | TpmObjectAttributes.Sign))
            .Sized([])
            .U16(TpmAlg.Null).U16(TpmAlg.EcDsa).U16(TpmAlg.Sha256).U16(TpmAlg.EccNistP256).U16(TpmAlg.Null)
            .Sized(unique).Sized([])
            .ToArray();
    }

    // ---- commands -----------------------------------------------------------------------------------------------------

    /// <summary>TPM2_CreatePrimary in <paramref name="hierarchy"/> from <paramref name="template"/> (a TPMT_PUBLIC); the caller flushes the handle.</summary>
    public TpmLoadedKey CreatePrimary(uint hierarchy, byte[] template)
    {
        var parameters = new TpmWriter()
            .U16(4).U16(0).U16(0) // TPM2B_SENSITIVE_CREATE: empty userAuth, empty data
            .Sized(template)
            .Sized([]) // outsideInfo
            .U32(0) // creationPCR: none
            .ToArray();
        var r = Run(CcCreatePrimary, [hierarchy], [Pw], parameters, handlesOut: 1, out var handles);
        var outPublic = r.Sized(4096);
        return new TpmLoadedKey(handles[0], outPublic);
    }

    /// <summary>TPM2_ReadPublic: the public area and the name of a loaded key.</summary>
    public (byte[] Public, byte[] Name) ReadPublic(uint handle)
    {
        var r = Run(CcReadPublic, [handle], [], [], 0, out _);
        var publicArea = r.Sized(4096);
        var name = r.Sized(128);
        return (publicArea, name);
    }

    /// <summary>
    /// TPM2_ActivateCredential: opens a credential made by TPM2_MakeCredential for <paramref name="endorsementKey"/> and the key at
    /// <paramref name="activateHandle"/> (both TPM2B, as Doppler sends them); the endorsement key is authorised by its policy.
    /// </summary>
    public byte[] ActivateCredential(uint activateHandle, uint endorsementKey, byte[] credentialBlob, byte[] encryptedSecret)
    {
        var session = StartPolicySession();
        try
        {
            Run(CcPolicySecret, [TpmHandle.Endorsement, session], [Pw],
                new TpmWriter().Sized([]).Sized([]).Sized([]).U32(0).ToArray(), 0, out _);
            var r = Run(CcActivateCredential, [activateHandle, endorsementKey], [Pw, PolicyAuth(session)],
                new TpmWriter().Sized(credentialBlob).Sized(encryptedSecret).ToArray(), 0, out _);
            return r.Sized(64);
        }
        finally { TryFlush(session); }
    }

    /// <summary>TPM2_Certify of <paramref name="objectHandle"/> by <paramref name="signHandle"/>: the TPMS_ATTEST and the TPMT_SIGNATURE.</summary>
    public (byte[] Attest, byte[] Signature) Certify(uint objectHandle, uint signHandle, byte[] qualifyingData, ushort scheme = TpmAlg.Null, ushort hash = 0)
    {
        var w = new TpmWriter().Sized(qualifyingData);
        Scheme(w, scheme, hash);
        var r = Run(CcCertify, [objectHandle, signHandle], [Pw, Pw], w.ToArray(), 0, out _);
        return (r.Sized(1024), SignatureBytes(r));
    }

    /// <summary>TPM2_Quote of the given PCRs of one bank by <paramref name="signHandle"/>: the TPMS_ATTEST and the TPMT_SIGNATURE.</summary>
    public (byte[] Attest, byte[] Signature) Quote(uint signHandle, byte[] qualifyingData, ushort bank, IReadOnlyList<int> pcrs, ushort scheme = TpmAlg.Null, ushort hash = 0)
    {
        var w = new TpmWriter().Sized(qualifyingData);
        Scheme(w, scheme, hash);
        Selection(w, bank, pcrs);
        var r = Run(CcQuote, [signHandle], [Pw], w.ToArray(), 0, out _);
        return (r.Sized(1024), SignatureBytes(r));
    }

    /// <summary>TPM2_Sign of a digest by an unrestricted ECDSA key (no hash ticket needed): r and s as IEEE P1363, 32 bytes each.</summary>
    public byte[] SignEcdsaP256(uint keyHandle, byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        var w = new TpmWriter().Sized(digest).U16(TpmAlg.EcDsa).U16(TpmAlg.Sha256)
            .U16(0x8024).U32(TpmHandle.Null).Sized([]); // TPMT_TK_HASHCHECK, the null ticket
        var r = Run(CcSign, [keyHandle], [Pw], w.ToArray(), 0, out _);
        if (r.U16() != TpmAlg.EcDsa) throw new TpmFormatException("not an ECDSA signature");
        r.U16();
        var sigR = r.Sized(48);
        var sigS = r.Sized(48);
        return [.. FixedWidth(sigR, 32), .. FixedWidth(sigS, 32)];
    }

    /// <summary>TPM2_PCR_Read of one PCR in one bank.</summary>
    public byte[] PcrRead(ushort bank, int pcr)
    {
        var w = new TpmWriter();
        Selection(w, bank, [pcr]);
        var r = Run(CcPcrRead, [], [], w.ToArray(), 0, out _);
        r.U32(); // update counter
        var count = r.U32();
        for (var i = 0; i < count; i++) { r.U16(); var size = r.U8(); r.Fixed(size); }
        if (r.U32() != 1) throw new TpmFormatException("the PCR was not read");
        return r.Sized(64);
    }

    /// <summary>TPM2_PCR_Extend of one PCR with one digest (tests and the software path only; firmware extends PCR 7 on a real machine).</summary>
    public void PcrExtend(int pcr, ushort bank, byte[] digest)
    {
        var w = new TpmWriter().U32(1).U16(bank).Raw(digest);
        Run(CcPcrExtend, [(uint)pcr], [Pw], w.ToArray(), 0, out _);
    }

    public void Flush(uint handle) => Run(CcFlushContext, [], [], new TpmWriter().U32(handle).ToArray(), 0, out _);

    public void TryFlush(uint handle)
    {
        try { Flush(handle); }
        catch (TpmCommandException) { }
    }

    // ---- marshalling --------------------------------------------------------------------------------------------------

    sealed record Auth(uint Handle);

    static readonly Auth Pw = new(TpmHandle.Password);

    static Auth PolicyAuth(uint session) => new(session);

    uint StartPolicySession()
    {
        var parameters = new TpmWriter()
            .Sized(new byte[32]) // nonceCaller: for an unsalted, unbound session it only has to be present; no secret is derived from it
            .Sized([]) // no salt
            .U8(0x01) // TPM_SE_POLICY
            .U16(TpmAlg.Null) // no parameter encryption
            .U16(TpmAlg.Sha256)
            .ToArray();
        var r = Run(CcStartAuthSession, [TpmHandle.Null, TpmHandle.Null], [], parameters, 1, out var handles);
        r.Sized(64);
        return handles[0];
    }

    static void Scheme(TpmWriter w, ushort scheme, ushort hash)
    {
        w.U16(scheme);
        if (scheme != TpmAlg.Null) w.U16(hash);
    }

    static void Selection(TpmWriter w, ushort bank, IReadOnlyList<int> pcrs)
    {
        var select = new byte[3];
        foreach (var p in pcrs)
        {
            if (p is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(pcrs));
            select[p / 8] |= (byte)(1 << (p % 8));
        }
        w.U32(1).U16(bank).U8(3).Raw(select);
    }

    static byte[] SignatureBytes(TpmReader r)
    {
        var start = r.Position;
        var alg = r.U16();
        switch (alg)
        {
            case TpmAlg.RsaSsa or TpmAlg.RsaPss:
                r.U16();
                r.Sized(512);
                break;
            case TpmAlg.EcDsa:
                r.U16();
                r.Sized(66);
                r.Sized(66);
                break;
            default:
                throw new TpmFormatException("unsupported signature algorithm");
        }
        return r.Slice(start, r.Position).ToArray();
    }

    static byte[] FixedWidth(byte[] value, int width)
    {
        var trimmed = value.AsSpan().TrimStart((byte)0);
        if (trimmed.Length > width) throw new TpmFormatException("signature component too long");
        var result = new byte[width];
        trimmed.CopyTo(result.AsSpan(width - trimmed.Length));
        return result;
    }

    TpmReader Run(uint code, uint[] handles, Auth[] auths, byte[] parameters, int handlesOut, out uint[] outHandles)
    {
        var body = new TpmWriter();
        foreach (var h in handles) body.U32(h);
        if (auths.Length > 0)
        {
            var area = new TpmWriter();
            foreach (var a in auths) area.U32(a.Handle).Sized([]).U8(0x01).Sized([]); // empty nonce, continueSession, empty password or HMAC
            var bytes = area.ToArray();
            body.U32((uint)bytes.Length).Raw(bytes);
        }
        body.Raw(parameters);
        var payload = body.ToArray();
        var command = new TpmWriter().U16(auths.Length > 0 ? Sessions : NoSessions).U32((uint)(10 + payload.Length)).U32(code).Raw(payload).ToArray();

        // A TPM may ask for the same command again (TPM_RC_RETRY, e.g. while it writes its dictionary-attack state to NV first;
        // TPM_RC_YIELDED; TPM_RC_TESTING during self test). The command did not run, so sending it again is safe; the tries are bounded.
        byte[] response;
        uint rc;
        var tries = 0;
        do
        {
            response = transport.Submit(command);
            if (response.Length < 10) throw new TpmFormatException("short TPM response");
            var size = BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(2, 4));
            if (size != response.Length) throw new TpmFormatException("TPM response size mismatch");
            rc = BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(6, 4));
        }
        while (rc is RcRetry or RcYielded or RcTesting && ++tries < MaxTries);
        if (rc != 0) throw new TpmCommandException(rc, code);

        var r = new TpmReader(response.AsMemory(10));
        outHandles = new uint[handlesOut];
        for (var i = 0; i < handlesOut; i++) outHandles[i] = r.U32();
        if (auths.Length > 0)
        {
            var parameterSize = (int)r.U32();
            var start = r.Position;
            return new TpmReader(r.Slice(start, start + parameterSize));
        }
        return r;
    }
}

/// <summary>
/// A TPM reached through a byte stream: swtpm's TCP server port (tests) or a Linux TPM resource manager device. One command at a
/// time; the response is read by its header's size.
/// </summary>
public sealed class StreamTpmTransport(Stream stream) : ITpmTransport, IDisposable
{
    readonly Lock gate = new();

    public byte[] Submit(byte[] command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (gate)
        {
            stream.Write(command);
            stream.Flush();
            var header = new byte[10];
            stream.ReadExactly(header);
            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2, 4));
            if (size is < 10 or > 65536) throw new TpmFormatException("bad TPM response size");
            var response = new byte[size];
            header.CopyTo(response, 0);
            stream.ReadExactly(response.AsSpan(10));
            return response;
        }
    }

    public void Dispose() => stream.Dispose();
}
