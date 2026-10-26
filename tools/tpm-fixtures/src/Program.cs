using System.Security.Cryptography;
using CM.BlackHole.Doppler.Tpm;

// make-credential <ek.pub (TPM2B_PUBLIC)> <ak.name> <out cred file (tpm2-tools format)> <out secret>
if (args is not ["make-credential", var ekPath, var namePath, var credPath, var secretPath])
{
    Console.Error.WriteLine("usage: make-credential <ek.pub> <ak.name> <cred out> <secret out>");
    return 2;
}
using var ek = TpmPublic.ParseSized(File.ReadAllBytes(ekPath)).RsaKey();
var secret = RandomNumberGenerator.GetBytes(32);
var sealedCredential = TpmCredential.Make(ek, File.ReadAllBytes(namePath), secret);
// tpm2-tools' credential file: magic 0xBADCC0DE, version 1, TPM2B_ID_OBJECT, TPM2B_ENCRYPTED_SECRET.
File.WriteAllBytes(credPath, [.. new TpmWriter().U32(0xBADCC0DE).U32(1).ToArray(), .. sealedCredential.CredentialBlob, .. sealedCredential.EncryptedSecret]);
File.WriteAllBytes(secretPath, secret);
return 0;
