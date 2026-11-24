using Microsoft.Extensions.Logging;

namespace ApiGovernance.Modules.IdentityAccess.Services;

public sealed record AuthTokenDeliveryContext(
    Guid TargetUserId,
    string TargetEmail,
    string TargetFullName,
    string Purpose,
    string SingleUseToken,
    DateTimeOffset ExpiresAtUtc,
    Guid? InitiatedByUserId);

/// <summary>
/// Abstraction for delivering single-use activation and password-reset tokens (04_Authentication.md).
/// Enterprise IT email / identity verification integration is intentionally deferred pending IT specification.
/// </summary>
public interface IAuthTokenDeliveryChannel
{
    string ChannelCode { get; }

    Task DeliverPasswordResetTokenAsync(
        AuthTokenDeliveryContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MVP token delivery placeholder when enterprise SMTP is deferred.
/// Never logs or exposes the plaintext token or password.
/// </summary>
public sealed class DeferredEnterpriseEmailDeliveryChannel(
    ILogger<DeferredEnterpriseEmailDeliveryChannel> logger) : IAuthTokenDeliveryChannel
{
    public string ChannelCode => "DEFERRED_ENTERPRISE_EMAIL_PENDING";

    public Task DeliverPasswordResetTokenAsync(
        AuthTokenDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        var maskedEmail = MaskEmail(context.TargetEmail);
        logger.LogInformation(
            "Single-use auth token generated for user {TargetUserId} ({MaskedEmail}) with purpose {Purpose} expiring at {ExpiresAtUtc}. Delivery deferred pending enterprise email integration.",
            context.TargetUserId,
            maskedEmail,
            context.Purpose,
            context.ExpiresAtUtc);

        return Task.CompletedTask;
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[atIndex..]}";
    }
}

