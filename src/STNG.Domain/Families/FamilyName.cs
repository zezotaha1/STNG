using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Values;

namespace STNG.Families;

public sealed class FamilyName : ValueObject, IEquatable<FamilyName>
{
    public string Value { get; }

    public FamilyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > FamilyConsts.MaxNameLength)
            throw new BusinessException(FamilyErrorCodes.InvalidName);
        Value = value.Trim();
    }

    protected override IEnumerable<object> GetAtomicValues() { yield return Value; }
    public bool Equals(FamilyName? other) => other != null && StringComparer.Ordinal.Equals(Value, other.Value);
    public override bool Equals(object? obj) => obj is FamilyName other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
}
