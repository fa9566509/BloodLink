namespace AF.Blood.Application.Domain;

/// <summary>
/// Legal state transitions for a donation request. Acceptance of a request by
/// a donor only moves <c>open → matched</c>; fulfilment is driven by donation
/// reporting, never by acceptance itself.
/// </summary>
public static class RequestStateMachine
{
    public const string Open = "open";
    public const string Matched = "matched";
    public const string Fulfilled = "fulfilled";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string UnderReview = "under_review";

    /// <summary>Transition → short description of what causes it. Used by both
    /// the runtime check and the documentation.</summary>
    public static readonly IReadOnlyDictionary<(string From, string To), string> Transitions =
        new Dictionary<(string, string), string>
        {
            [(Open, Matched)] = "a donor's acceptance was recorded (single donor slot)",
            [(Open, Cancelled)] = "the request owner cancelled the request",
            [(Open, Expired)] = "server-side expiry processing reached expires_at_utc",
            [(Matched, Fulfilled)] = "a donation outcome was reported for the request",
            [(Matched, Cancelled)] = "the request owner cancelled the request",
            [(Matched, UnderReview)] = "the requester disputed the donation report",
            [(Fulfilled, UnderReview)] = "the requester disputed the donation report",
            [(UnderReview, Fulfilled)] = "moderation upheld the donation report",
            [(UnderReview, Cancelled)] = "moderation rejected the donation report",
            [(UnderReview, Open)] = "moderation dismissed the dispute as unfounded; donor slot released",
        };

    public static bool CanTransition(string from, string to) => Transitions.ContainsKey((from, to));

    /// <summary>Statuses from which no further waves or notifications should run.</summary>
    public static bool IsClosed(string status) =>
        status is Cancelled or Expired or Fulfilled;

    /// <summary>Whether the request can accept a new donor response right now.</summary>
    public static bool AcceptsResponses(string status, DateTimeOffset expiresAtUtc, DateTimeOffset now) =>
        status == Open && expiresAtUtc > now;

    /// <summary>Whether a donation report may be filed against this request.</summary>
    public static bool AcceptsDonationReport(string status) =>
        status is Matched or Fulfilled;
}
