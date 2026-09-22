using System;
using Volo.Abp.Timing;

namespace STNG.Authentication;

public class TestClock : IClock
{
    public DateTime Now { get; private set; } = DateTime.UtcNow;
    public DateTimeKind Kind => DateTimeKind.Utc;
    public bool SupportsMultipleTimezone => false;
    public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    public DateTime Normalize(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public DateTime ConvertToUserTime(DateTime value) => value;
    public DateTimeOffset ConvertToUserTime(DateTimeOffset value) => value;
    public DateTime ConvertToUtc(DateTime value) => Normalize(value);
}
