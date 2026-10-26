namespace AF.Blood.Application.Domain;

/// <summary>
/// Legal transitions for donation records. Provenance rule: a self-reported
/// donation is never treated as medically verified — verification is a
/// separate, explicit act by a donation verifier.
/// </summary>
public static class DonationStateMachine
{
    public const string Reported = "reported";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string CompletedUnverified = "completed_unverified";
    public const string Verified = "verified";
    public const string Disputed = "disputed";
    public const string Rejected = "rejected";
    public const string Corrected = "corrected";

    /// <summary>v1 initial state is <see cref="Reported"/>; the requester is asked
    /// to acknowledge. <see cref="AwaitingConfirmation"/> is reserved for v2
    /// acknowledgement-escalation flows and is not produced by v1 logic.</summary>
    public static readonly IReadOnlyDictionary<(string From, string To), string> Transitions =
        new Dictionary<(string, string), string>
        {
            [(Reported, CompletedUnverified)] = "requester acknowledged the donation",
            [(Reported, Disputed)] = "requester disputed the report",
            [(Reported, Rejected)] = "moderator rejected the report",
            [(Reported, Verified)] = "donation verifier confirmed the donation independently",
            [(CompletedUnverified, Verified)] = "donation verifier confirmed the donation independently",
            [(CompletedUnverified, Disputed)] = "requester disputed their earlier acknowledgement",
            [(CompletedUnverified, Rejected)] = "moderator rejected the report",
            [(Disputed, CompletedUnverified)] = "moderation resolved the dispute in favour of the report",
            [(Disputed, Rejected)] = "moderation resolved the dispute against the report",
            [(Disputed, Verified)] = "moderation resolved the dispute and verified the donation",
            // 'corrected' is applied by administrative correction; it records that
            // values were amended and the resulting state is authoritative.
            [(Verified, Corrected)] = "administrator corrected the record",
            [(CompletedUnverified, Corrected)] = "administrator corrected the record",
            [(Reported, Corrected)] = "administrator corrected the record",
            [(Disputed, Corrected)] = "administrator corrected the record",
            [(Corrected, Verified)] = "donation verifier confirmed the corrected record",
            [(Corrected, Rejected)] = "moderator rejected the corrected record",
        };

    public static bool CanTransition(string from, string to) => Transitions.ContainsKey((from, to));

    /// <summary>States in which the donation is treated as having happened
    /// (deferral policy applies to these, never to disputed/rejected alone).</summary>
    public static bool CountsAsDonation(string status) =>
        status is Reported or AwaitingConfirmation or CompletedUnverified or Verified or Corrected;
}
