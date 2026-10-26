namespace AF.Blood.Contracts;

// --- Requests -----------------------------------------------------------------

public record UpsertDonorProfileRequest(string BloodGroup, int SearchRadiusKm);

/// <summary>Accepting discovery consent records the version, the timestamp and an
/// approximate location. With <c>Accept = false</c> discovery is switched off and
/// consent fields are cleared.</summary>
public record DonorConsentRequest(bool Accept, string ConsentVersion, double? Latitude, double? Longitude);

public record DonorAvailabilityRequest(bool IsAvailable);

// --- Responses ----------------------------------------------------------------

/// <summary>Coordinates in donor-facing DTOs are the generalized (approximate)
/// points stored server-side; precise locations never leave the database.</summary>
public record DonorProfileDto(
    Guid UserId,
    string BloodGroup,
    bool IsDiscoverable,
    bool IsAvailable,
    double? ApproxLatitude,
    double? ApproxLongitude,
    int SearchRadiusKm,
    string? ConsentVersion,
    DateTimeOffset? ConsentedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public record DonorEligibilityDto(
    bool IsDiscoverable,
    bool IsAvailable,
    bool IsDeferred,
    string? DeferralType,
    DateTimeOffset? EligibleAgainAtUtc,
    string? PolicyVersion,
    int? DeferralRemainingDays,
    string Message);

public record NearbyRequestDto(
    Guid RequestId,
    string BloodGroup,
    string Urgency,
    string Status,
    string DestinationLabel,
    double ApproxLatitude,
    double ApproxLongitude,
    double DistanceKm,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
