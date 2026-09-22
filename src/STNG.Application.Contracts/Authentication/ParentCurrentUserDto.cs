using System;

namespace STNG.Authentication;

public class ParentCurrentUserDto
{
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Job { get; set; }
    public bool EmailConfirmed { get; set; }
}
