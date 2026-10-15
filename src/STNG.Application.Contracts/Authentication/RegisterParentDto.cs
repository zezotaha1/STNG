using System;
using Volo.Abp.Auditing;

namespace STNG.Authentication;

[DisableAuditing]
public class RegisterParentDto
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Job { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string PhoneCountryIso2 { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
}
