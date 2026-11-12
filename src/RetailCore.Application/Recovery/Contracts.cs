using RetailCore.Application.Foundation;
namespace RetailCore.Application.Recovery;

public sealed record BackupSettings(string PrimaryDirectory, string? SecondDirectory, int RetainCount = 30);
public sealed record BackupStatus(BackupSettings Settings, DateTimeOffset? LastAttempt = null,
    DateTimeOffset? LastPrimarySuccess = null, DateTimeOffset? LastSecondSuccess = null,
    string State = "BackupNever", string? LastFile = null, string? LastDailyDate = null, string? LastMaintenanceState = null, DateTimeOffset? LastMaintenanceAt = null, string? LastSafetyFile = null);
public sealed record RestoreReview(string Path, string Sha256, string Shop, int Bills, int Movements, int Shifts, DateTimeOffset ArchiveWrittenAt);
public sealed record RestoreRequest(Guid Id, string Path, string Sha256, string Reason);
public sealed record RecoveryActor(Guid ShopId, Guid UserId, Guid OwnerId, string Name, string? OwnerName = null);
public interface IRecoveryStorage
{
    Task<BackupStatus> StatusAsync(Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct);
    Task<BackupStatus> ConfigureAsync(BackupSettings settings, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct);
    Task<BackupStatus> BackupAsync(Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct);
    Task<RestoreReview> ReviewAsync(string path, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct);
    Task RestoreAsync(RestoreRequest request, Func<ILocalStore, CancellationToken, Task<RecoveryActor>> authorize, CancellationToken ct);
}
