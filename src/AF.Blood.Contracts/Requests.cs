namespace AF.Blood.Contracts;

// --- Requests -----------------------------------------------------------------

public record CreateRequestRequest(
    string BloodGroup,
    double Latitude,
    double Longitude,
    string DestinationLabel,
    string? FacilityName,
    string? ReferenceNote,
    string Urgency,
    int? ExpiresInHours);

public record RespondToRequestRequest(string Response); // accepted | declined

public record DonationReportRequest(DateOnly DonationDate, string? Notes);

public record AcknowledgementRequest(bool Acknowledge, string? Note);

// --- Responses ----------------------------------------------------------------

/// <summary>Owner and participant view. <c>ApproxLatitude/Longitude</c> are the
/// generalized destination coordinates (2 decimal places).</summary>
public record RequestDto(
    Guid Id,
    Guid RequesterId,
    string RequesterDisplayName,
    string BloodGroup,
    string ComponentType,
    string DestinationLabel,
    double ApproxLatitude,
    double ApproxLongitude,
    string? FacilityName,
    string? ReferenceNote,
    string Urgency,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    Guid? MatchedDonorUserId,
    string? MatchedDonorDisplayName,
    double? DistanceKm,
    int AcceptedCount,
    bool MyResponseAccepted);

public record ResponseDto(
    Guid Id,
    Guid RequestId,
    Guid DonorUserId,
    string DonorDisplayName,
    string Response,
    DateTimeOffset CreatedAtUtc);

public record DonationRecordDto(
    Guid Id,
    Guid? RequestId,
    Guid DonorUserId,
    Guid ReportedByUserId,
    DateOnly DonationDate,
    string Status,
    string? PolicyVersion,
    DateTimeOffset? EligibleAgainAtUtc,
    Guid? ConfirmedByUserId,
    DateTimeOffset? ConfirmedAtUtc,
    string? Notes,
    DateTimeOffset CreatedAtUtc);
