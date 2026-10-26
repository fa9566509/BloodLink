namespace AF.Blood.Contracts;

// --- Abuse reports ------------------------------------------------------------

public record CreateAbuseReportRequest(Guid? TargetUserId, Guid? TargetRequestId, string Category, string Details);

public record AbuseReportDto(
    Guid Id,
    Guid ReportedByUserId,
    Guid? TargetUserId,
    Guid? TargetRequestId,
    string Category,
    string Details,
    string Status,
    string? ResolutionNote,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public record ResolveAbuseReportRequest(string Outcome, string Note); // resolved | dismissed

// --- Account suspension -------------------------------------------------------

public record SuspendUserRequest(string Reason);

// --- Deferral policies --------------------------------------------------------

public record CreatePolicyVersionRequest(
    string Version,
    int DeferralDays,
    int ProvisionalDays,
    DateOnly EffectiveFrom,
    string ApprovedBy,
    string ApprovalReference,
    string? Notes);

public record PolicyVersionDto(
    Guid Id,
    string Version,
    string ComponentType,
    int DeferralDays,
    int ProvisionalDays,
    DateOnly EffectiveFrom,
    bool IsActive,
    string ApprovedBy,
    string ApprovalReference,
    string? Notes,
    DateTimeOffset CreatedAtUtc);

// --- Bootstrap ----------------------------------------------------------------

public record AdminBootstrapRequest(string Key);

// --- Donation moderation ------------------------------------------------------

public record AdminDonationDecisionRequest(string Note, DateOnly? CorrectedDonationDate);
