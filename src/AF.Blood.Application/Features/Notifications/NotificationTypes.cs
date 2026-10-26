namespace AF.Blood.Application.Features.Notifications;

public static class NotificationTypes
{
    public const string RequestMatchedDonorAlert = "request_match_alert";
    public const string DonorAccepted = "donor_accepted";
    public const string RequestCancelled = "request_cancelled";
    public const string DonationReported = "donation_reported";
    public const string DonationAcknowledged = "donation_acknowledged";
    public const string DonationDisputed = "donation_disputed";
    public const string DonationVerified = "donation_verified";
    public const string DonationRejected = "donation_rejected";
    public const string DonationCorrected = "donation_corrected";
    public const string AccountSuspended = "account_suspended";
    public const string AccountRestored = "account_restored";
}
