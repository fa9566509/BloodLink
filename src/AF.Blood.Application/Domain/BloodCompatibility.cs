namespace AF.Blood.Application.Domain;

/// <summary>
/// Red-cell blood-group compatibility (ABO + Rh). This is the single source of
/// truth used by matching; the database table <c>blood_group_compatibility</c>
/// (seeded in V003) mirrors it and an integration test asserts both agree.
/// Medical source: standard ABO/Rh red-cell compatibility. Any change to
/// clinical policy must come from a qualified transfusion professional.
/// </summary>
public static class BloodCompatibility
{
    /// <summary>Donor groups whose red cells may be given to the recipient group.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RecipientToDonors =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["O-"] = new HashSet<string> { "O-" },
            ["O+"] = new HashSet<string> { "O-", "O+" },
            ["A-"] = new HashSet<string> { "O-", "A-" },
            ["A+"] = new HashSet<string> { "O-", "O+", "A-", "A+" },
            ["B-"] = new HashSet<string> { "O-", "B-" },
            ["B+"] = new HashSet<string> { "O-", "O+", "B-", "B+" },
            ["AB-"] = new HashSet<string> { "O-", "A-", "B-", "AB-" },
            ["AB+"] = new HashSet<string> { "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+" },
        };

    public static bool IsCompatible(string recipientGroup, string donorGroup) =>
        RecipientToDonors.TryGetValue(recipientGroup, out var donors) && donors.Contains(donorGroup);

    /// <summary>Donor groups that can donate to the given recipient group (order stable).</summary>
    public static IReadOnlyList<string> CompatibleDonorGroups(string recipientGroup)
    {
        if (!RecipientToDonors.TryGetValue(recipientGroup, out var donors))
        {
            throw new ArgumentException($"Unknown blood group '{recipientGroup}'.", nameof(recipientGroup));
        }

        // Iterate the canonical order so SQL parameter lists are deterministic.
        return Domain.BloodGroups.All.Where(donors.Contains).ToArray();
    }

    /// <summary>Request (recipient) groups the given donor may help with, in canonical order.</summary>
    public static IReadOnlyList<string> CompatibleDonorGroupsForDonor(string donorGroup)
    {
        if (!Domain.BloodGroups.IsValid(donorGroup))
        {
            throw new ArgumentException($"Unknown blood group '{donorGroup}'.", nameof(donorGroup));
        }

        return Domain.BloodGroups.All.Where(r => RecipientToDonors[r].Contains(donorGroup)).ToArray();
    }
}
