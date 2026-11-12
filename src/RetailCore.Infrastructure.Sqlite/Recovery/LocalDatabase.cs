using System.Collections.Concurrent;
using RetailCore.Application.Foundation;
namespace RetailCore.Infrastructure.Sqlite.Recovery;

/// <summary>One lock per canonical database across DI providers, plus an OS file lock across processes.
/// Every local unit of work participates; no tracked context survives a maintenance operation.</summary>
public sealed class LocalDatabase(string path)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string StateDirectory => Path + ".recovery";
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(Path, _ => new SemaphoreSlim(1)); await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                try { return new Lease(gate, new FileStream(Path + ".maintenance", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
                catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(50, ct).ConfigureAwait(false); }
            }
        }
        catch { gate.Release(); throw; }
    }
    private sealed class Lease(SemaphoreSlim gate, FileStream file) : IDisposable
    {
        public void Dispose() { file.Dispose(); gate.Release(); }
    }
    public void RequireReady()
    {
        if (File.Exists(System.IO.Path.Combine(StateDirectory, "pending.json"))) throw new FoundationException("RecoveryPending");
    }
}
