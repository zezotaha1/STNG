using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Services;

namespace STNG.Authentication;

public class EmailOtpManager : DomainService
{
    private readonly IOptions<EmailOtpOptions> _options;
    public EmailOtpManager(IOptions<EmailOtpOptions> options) => _options = options;

    public (EmailVerificationOtp Otp, string Code) Create(Guid userId)
    {
        var options = _options.Value;
        var id = GuidGenerator.Create();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        return (new EmailVerificationOtp(id, userId, EmailVerificationOtp.ParentRegistration,
            Hash(id, userId, code), Clock.Now, TimeSpan.FromMinutes(options.LifetimeMinutes), options.MaxAttempts), code);
    }

    public bool Matches(EmailVerificationOtp otp, string code) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(otp.CodeHash), Convert.FromHexString(Hash(otp.Id, otp.UserId, code)));

    private string Hash(Guid id, Guid userId, string code)
    {
        var key = Convert.FromBase64String(_options.Value.HashKey);
        if (key.Length < 32) throw new InvalidOperationException("EmailOtp:HashKey requires at least 32 random bytes.");
        try
        {
            return Convert.ToHexString(HMACSHA256.HashData(key,
                Encoding.UTF8.GetBytes($"{id:N}:{userId:N}:{EmailVerificationOtp.ParentRegistration}:{code}")));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
