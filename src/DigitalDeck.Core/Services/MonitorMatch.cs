using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Services;

/// <summary>
/// Works out which of today's displays is the one a window was left on.
/// </summary>
/// <remarks>
/// A saved monitor is not an identity, it is a description, and every part of that description can
/// have changed by the next launch: a display can be unplugged, renumbered, moved to the other side
/// of the desk, or replaced by a different one in the same slot. So the match is a ladder from the
/// most specific evidence to the least, and the bottom rung is the primary display rather than
/// nothing — a window that cannot be put back where it was still has to open somewhere a person can
/// see it.
/// <para>
/// Plain logic over a list, deliberately kept out of the platform code: this is the part that is
/// worth testing, and it needs no display server to run.
/// </para>
/// </remarks>
public static class MonitorMatch
{
    /// <summary>The primary display, or the first one, or null when there are none at all.</summary>
    public static MonitorInfo? Primary(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.Count == 0 ? null : monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

    /// <summary>
    /// The display that best matches a remembered one.
    /// </summary>
    /// <param name="deviceName">The name it had when it was saved, if any.</param>
    /// <param name="savedBounds">The rectangle it had when it was saved, if any.</param>
    public static MonitorInfo? FindBest(
        IReadOnlyList<MonitorInfo> monitors,
        string? deviceName,
        MonitorRect? savedBounds)
    {
        if (monitors.Count == 0) return null;

        // Same slot and same geometry is as close to a stable identity as any desktop offers.
        if (deviceName is { Length: > 0 } &&
            monitors.FirstOrDefault(m => m.DeviceName == deviceName) is { } byName)
        {
            if (savedBounds is not { } bounds || SameSize(byName.Bounds, bounds)) return byName;
        }

        // The name moved, but a display is sitting exactly where that one was.
        if (savedBounds is { } saved &&
            monitors.FirstOrDefault(m => m.Bounds == saved) is { } byBounds)
            return byBounds;

        // Nothing is in that place any more, but something is the same size — the desk was
        // rearranged rather than the monitor replaced.
        if (savedBounds is { } size &&
            monitors.FirstOrDefault(m => SameSize(m.Bounds, size)) is { } bySize)
            return bySize;

        return Primary(monitors);
    }

    private static bool SameSize(MonitorRect a, MonitorRect b) =>
        a.Width == b.Width && a.Height == b.Height;
}
