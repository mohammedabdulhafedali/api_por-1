namespace ApiGovernance.Shared.Contracts;

public static class WorkflowRunStatusValues
{
    public const string InProgress = "IN_PROGRESS";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
}

public static class WorkflowStageStatusValues
{
    public const string Pending = "PENDING";
    public const string Active = "ACTIVE";
    public const string Approved = "APPROVED";
    public const string Returned = "RETURNED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";
}

public static class WorkflowDecisionValues
{
    public const string Approve = "APPROVE";
    public const string Return = "RETURN";
    public const string Reject = "REJECT";

    public static bool IsValid(string? value) =>
        string.Equals(value, Approve, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Return, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Reject, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

/// <summary>
/// Cross-module workflow starter invoked inside the caller's DB transaction
/// when submitting a documentation package revision (W01) or change request (W06).
/// </summary>
public interface IWorkflowRunStarter
{
    Task<Guid> StartRevisionWorkflowRunAsync(
        Guid revisionId,
        Guid routeId,
        Guid actorUserId,
        Guid integrationId,
        CancellationToken cancellationToken = default);
}
