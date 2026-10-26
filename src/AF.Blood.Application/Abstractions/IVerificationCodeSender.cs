namespace AF.Blood.Application.Abstractions;

public enum VerificationPurpose
{
    EmailVerification,
    PhoneVerification,
    PasswordReset,
}

/// <summary>Delivers verification/recovery codes. The development implementation
/// logs the code; production must configure a real email/SMS provider.</summary>
public interface IVerificationCodeSender
{
    Task SendAsync(VerificationPurpose purpose, string destination, string code, CancellationToken cancellationToken = default);
}
