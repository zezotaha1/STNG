using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Services;

namespace STNG.Families;

// These are generated capabilities (100/256 random bits), not human passwords.
// A domain-separated SHA-256 hash permits indexed lookup without plaintext storage.
public class FamilyJoinSecrets : DomainService
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string Generate(FamilyJoinCredentialKind kind)
    {
        if (kind == FamilyJoinCredentialKind.QRToken)
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (kind != FamilyJoinCredentialKind.ManualCode) throw new BusinessException(FamilyJoinErrorCodes.InvalidInput);
        var chars = new char[FamilyJoinConsts.ManualCodeLength];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var code = new string(chars);
        return string.Join("-", Enumerable.Range(0, 4).Select(i => code.Substring(i * 5, 5)));
    }

    public bool TryHash(FamilyJoinCredentialKind kind, string? secret, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > FamilyJoinConsts.MaxSecretInputLength) return false;
        string value;
        if (kind == FamilyJoinCredentialKind.ManualCode)
        {
            value = new string(secret.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            if (value.Length != FamilyJoinConsts.ManualCodeLength || value.Any(c => !Alphabet.Contains(c))) return false;
        }
        else if (kind == FamilyJoinCredentialKind.QRToken)
        {
            value = secret;
            if (value.Length != FamilyJoinConsts.QRTokenLength || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return false;
        }
        else return false;
        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"STNG.Family.Join:{(int)kind}:{value}")));
        return true;
    }

    public bool Matches(FamilyJoinCredential credential, FamilyJoinCredentialKind kind, string secret) =>
        credential.Kind == kind && TryHash(kind, secret, out var hash) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(credential.SecretHash), Convert.FromHexString(hash));
}
