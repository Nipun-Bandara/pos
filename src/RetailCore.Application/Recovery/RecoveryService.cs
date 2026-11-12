using System.Text.Json;
using RetailCore.Application.Foundation;
namespace RetailCore.Application.Recovery;

/// <summary>All interactive maintenance commands authorize against the live database under the maintenance lock.</summary>
public sealed class RecoveryService(IRecoveryStorage storage, FoundationService foundation, LocalSession session)
{
    public static ApprovalRequest RestoreApproval(RestoreRequest request) => new("RestoreDatabase", JsonSerializer.Serialize(request));
    private async Task<RecoveryActor> OwnerAsync(ILocalStore store, CancellationToken ct)
    {
        var owner = await foundation.RequireUserAsync(store, true, ct).ConfigureAwait(false);
        return new(owner.ShopId.Value, owner.Id.Value, owner.Id.Value, owner.DisplayName, owner.DisplayName);
    }
    public Task<BackupStatus> StatusAsync(CancellationToken ct = default) => storage.StatusAsync(OwnerAsync, ct);
    public Task<BackupStatus> ConfigureAsync(BackupSettings settings, CancellationToken ct = default) => storage.ConfigureAsync(settings, OwnerAsync, ct);
    public Task<BackupStatus> BackupAsync(CancellationToken ct = default) => storage.BackupAsync(OwnerAsync, ct);
    public Task<RestoreReview> ReviewAsync(string path, CancellationToken ct = default) => storage.ReviewAsync(path, OwnerAsync, ct);
    public Task RestoreAsync(RestoreRequest request, ApprovalTicket? ticket, CancellationToken ct = default)
    {
        if (request.Id == Guid.Empty || request.Sha256.Length != 64 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new FoundationException("InvalidRestore");
        var validate = foundation.ApprovalValidator(ticket, RestoreApproval(request)); var version = session.Version;
        return storage.RestoreAsync(request, async (store, token) =>
        {
            var actor = await foundation.RequireUserAsync(store, true, token).ConfigureAwait(false);
            if (session.Version != version) throw new FoundationException("SignInRequired");
            var terminal = (await store.TerminalsAsync(actor.ShopId, token).ConfigureAwait(false)).Single(t => t.IsActive);
            if (await store.OpenShiftAsync(actor.ShopId, terminal.Id, token).ConfigureAwait(false) is not null) throw new FoundationException("RestoreOpenShift");
            var owner = await validate(store, actor, token).ConfigureAwait(false);
            // Every attempted authorized restore invalidates all current process credentials.
            session.SignOut();
            var approvingOwner=(await store.UsersAsync(actor.ShopId,token).ConfigureAwait(false)).Single(u=>u.Id==owner);
            return new(actor.ShopId.Value, actor.Id.Value, owner.Value, actor.DisplayName,approvingOwner.DisplayName);
        }, ct);
    }
}
