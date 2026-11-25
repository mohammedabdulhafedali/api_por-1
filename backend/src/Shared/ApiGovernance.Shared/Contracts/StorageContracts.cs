namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Cross-module read-only verified access to private immutable file objects (V01 / P04).
/// Implemented by VersionsFiles so PublishingPortal can serve SHA-256 verified downloads
/// without breaking modular boundaries or duplicating storage code.
/// </summary>
public interface IPublishedObjectReadAccessor
{
    Task<(Stream Stream, long SizeBytes)> OpenVerifiedObjectStreamAsync(
        string storageKey,
        string expectedSha256,
        CancellationToken cancellationToken = default);
}
