namespace RetailCore.Application.Abstractions;

/// <summary>
/// The current time, injected rather than read from <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
/// <remarks>
/// Shift boundaries, effective-dated tax rates and receipt timestamps all turn on what time
/// it is. Taking it as a dependency is what makes "the rate changed at midnight" and "the
/// shift rolled over" testable without waiting for midnight.
/// </remarks>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
