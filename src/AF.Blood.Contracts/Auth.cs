namespace AF.Blood.Contracts;

// --- Requests -----------------------------------------------------------------

public record RegisterRequest(string Email, string Password, string DisplayName, string? PhoneNumber);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record SendVerificationRequest(string Purpose); // email_verification | phone_verification
public record ConfirmVerificationRequest(string Purpose, string Code);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// --- Responses ----------------------------------------------------------------

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    UserDto User);
