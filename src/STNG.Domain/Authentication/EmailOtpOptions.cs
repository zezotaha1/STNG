using System;
using System.ComponentModel.DataAnnotations;

namespace STNG.Authentication;

public class EmailOtpOptions
{
    [Range(1, 60)] public int LifetimeMinutes { get; set; } = 10;
    [Range(1, 10)] public int MaxAttempts { get; set; } = 5;
    [Range(60, 3600)] public int ResendCooldownSeconds { get; set; } = 60;
    // Supply at least 32 random bytes, base64 encoded, through secret configuration.
    public string HashKey { get; set; } = "";
}
