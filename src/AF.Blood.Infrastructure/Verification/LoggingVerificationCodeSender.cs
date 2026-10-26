using AF.Blood.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AF.Blood.Infrastructure.Verification;

/// <summary>Development verification-code sender: codes go to the log. A
/// production email/SMS provider must be wired before public launch
/// (docs/launch-configuration.md).</summary>
public sealed class LoggingVerificationCodeSender(ILogger<LoggingVerificationCodeSender> logger) : IVerificationCodeSender
{
    public Task SendAsync(VerificationPurpose purpose, string destination, string code, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "DEV-ONLY verification code for {Purpose} to {Destination}: {Code}. Configure a real provider before production.",
            purpose, destination, code);
        return Task.CompletedTask;
    }
}
