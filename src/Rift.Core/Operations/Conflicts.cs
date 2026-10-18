namespace Rift.Core.Operations;

public enum ConflictAction
{
    Replace,
    Skip,
    KeepBoth,
    Cancel,
}

public readonly record struct ConflictDecision(ConflictAction Action, bool ApplyToAll = false);

/// <summary>A file is about to land on an existing file with the same name.</summary>
public sealed record FileConflict(
    string SourcePath,
    string DestinationPath,
    long SourceSize,
    DateTime SourceModified,
    long DestinationSize,
    DateTime DestinationModified);

public interface IConflictResolver
{
    Task<ConflictDecision> ResolveAsync(FileConflict conflict, CancellationToken ct);
}

/// <summary>Always answers the same; used by tests, undo and "paste into the same folder".</summary>
public sealed class FixedConflictResolver(ConflictAction action) : IConflictResolver
{
    public Task<ConflictDecision> ResolveAsync(FileConflict conflict, CancellationToken ct) =>
        Task.FromResult(new ConflictDecision(action, ApplyToAll: true));
}
