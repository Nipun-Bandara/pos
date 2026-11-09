using RetailCore.Domain.Common;

namespace RetailCore.Application.Foundation;

/// <summary>Process-local identity, never a caller-supplied role. Commands reload the active user.</summary>
public sealed class LocalSession
{
    public UserId? UserId { get; private set; }
    public bool IsLocked { get; private set; } = true;
    public Guid Version { get; private set; } = Guid.NewGuid();

    internal string? CredentialHash { get; private set; }

    internal void SignIn(UserId id, string credentialHash)
    {
        UserId = id;
        CredentialHash = credentialHash;
        IsLocked = false;
        Version = Guid.NewGuid();
    }

    public void Lock()
    {
        IsLocked = true;
        Version = Guid.NewGuid();
    }

    public void SignOut()
    {
        UserId = null;
        CredentialHash = null;
        Lock();
    }
}
