using System;

namespace STNG.Families;

public class FamilyJoinOptions
{
    public const string SectionName = "Family:Join";
    public TimeSpan ManualCodeLifetime { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan QRTokenLifetime { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan ReapplyCooldown { get; set; } = TimeSpan.FromMinutes(1);
    public int AttemptsPerParentPerMinute { get; set; } = 10;
    public int AttemptsPerIpPerMinute { get; set; } = 30;
    public string QRJoinBaseUri { get; set; } = "stng://family/join";

    public bool IsValid() =>
        ManualCodeLifetime > TimeSpan.Zero && ManualCodeLifetime <= TimeSpan.FromDays(30) &&
        QRTokenLifetime > TimeSpan.Zero && QRTokenLifetime <= TimeSpan.FromDays(30) &&
        ReapplyCooldown >= TimeSpan.Zero && ReapplyCooldown <= TimeSpan.FromDays(1) &&
        AttemptsPerParentPerMinute > 0 && AttemptsPerIpPerMinute > 0 &&
        Uri.TryCreate(QRJoinBaseUri, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || uri.Scheme == "stng") && !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
