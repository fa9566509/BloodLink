namespace AF.Blood.Application.Domain;

public static class BloodGroups
{
    public static readonly IReadOnlyList<string> All =
        ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    public static bool IsValid(string? group) => group is not null && All.Contains(group);
}
