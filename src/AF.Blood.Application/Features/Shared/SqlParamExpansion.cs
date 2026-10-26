using Dapper;

namespace AF.Blood.Application.Features.Shared;

/// <summary>Helpers for binding list values to PostgreSQL queries. Dapper cannot
/// bind .NET arrays directly (no provider type mapping), so we expand
/// <c>= any(@p)</c> into <c>= any(array[@p0, @p1, …])</c> with one parameter per
/// element — still fully parameterised, no string interpolation of values.</summary>
public static class SqlParamExpansion
{
    public static (string SqlFragment, DynamicParameters Parameters) AnyOf<T>(
        string paramNamePrefix, IReadOnlyList<T> values)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var names = new string[values.Count];
        var parameters = new DynamicParameters();
        for (var i = 0; i < values.Count; i++)
        {
            names[i] = $"{paramNamePrefix}{i}";
            parameters.Add(names[i], values[i]);
        }

        var fragment = $"any(array[{string.Join(", ", names.Select(n => "@" + n))}])";
        return (fragment, parameters);
    }
}
