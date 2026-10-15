using System;
using Volo.Abp.Timing;

namespace STNG.Authentication;

public class TestClock : IClock
{
    public DateTime Now { get; private set; } = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    public DateTimeKind Kind => DateTimeKind.Utc;
    public bool SupportsMultipleTimezone => false;
    public void Set(DateTime value) => Now = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public void Advance(TimeSpan duration) => Now = Now.Add(duration);
    public DateTime Normalize(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public DateTime ConvertToUserTime(DateTime value) => value;
    public DateTimeOffset ConvertToUserTime(DateTimeOffset value) => value;
    public DateTime ConvertToUtc(DateTime value) => Normalize(value);
}
