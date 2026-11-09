namespace RetailCore.Infrastructure.Sqlite.Foundation;

/// <summary>Local completed-command receipt; contains safe results, never credentials.</summary>
public sealed class CommandRecord
{
    public Guid Id { get; private set; }
    public string Kind { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public string ResultJson { get; private set; } = "";

    private CommandRecord() { }

    internal CommandRecord(Guid id, string kind, string fingerprint, string resultJson)
    {
        Id = id;
        Kind = kind;
        Fingerprint = fingerprint;
        ResultJson = resultJson;
    }
}
