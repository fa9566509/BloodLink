namespace AF.Blood.Contracts;

/// <summary>Reference values shared across API contracts. Blood groups are
/// plain strings (validated server-side) so clients do not need enum coupling.</summary>
public static class BloodGroups
{
    public static readonly IReadOnlyList<string> All =
        ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    public static bool IsValid(string? group) => group is not null && All.Contains(group);
}

public static class Urgencies
{
    public const string Routine = "routine";
    public const string Urgent = "urgent";
    public const string Critical = "critical";

    public static readonly IReadOnlyList<string> All = [Routine, Urgent, Critical];

    public static bool IsValid(string? urgency) => urgency is not null && All.Contains(urgency);
}

public static class RequestStatuses
{
    public const string Open = "open";
    public const string Matched = "matched";
    public const string Fulfilled = "fulfilled";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string UnderReview = "under_review";
}

public static class ResponseTypes
{
    public const string Accepted = "accepted";
    public const string Declined = "declined";

    public static bool IsValid(string? response) => response is Accepted or Declined;
}

public static class DonationStatuses
{
    public const string Reported = "reported";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string CompletedUnverified = "completed_unverified";
    public const string Verified = "verified";
    public const string Disputed = "disputed";
    public const string Rejected = "rejected";
    public const string Corrected = "corrected";
}

public static class DeferralTypes
{
    public const string Provisional = "provisional";
    public const string Verified = "verified";
    public const string Administrative = "administrative";
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
