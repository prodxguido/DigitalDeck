namespace DigitalDeck.Core.Model;

/// <summary>What kind of channel a fader drives.</summary>
/// <remarks>
/// The distinction is not decoration: a group and a return sit in different places in Live and are
/// reached through different halves of the index space, and the mixer groups them on screen the way
/// Live's own mixer does rather than showing one undifferentiated row of faders.
/// </remarks>
public enum LiveTrackKind
{
    /// <summary>An ordinary audio or MIDI track.</summary>
    Normal,

    /// <summary>A group track. Has a fader of its own that rides everything folded under it.</summary>
    Group,

    /// <summary>A return track, fed by the sends.</summary>
    Return,

    /// <summary>The master. Exactly one of these, and always last.</summary>
    Master
}

/// <summary>
/// One channel of Live's mixer, as the app knows it.
/// </summary>
/// <param name="Index">
/// What to send back to move this channel. Positional and therefore not an identity: deleting a
/// track renumbers everything after it, which is why the roster arrives whole rather than as
/// patches.
/// </param>
/// <param name="Name">The track's name in Live, at the moment the roster was built.</param>
/// <param name="Kind">Where in the mixer this channel belongs.</param>
/// <param name="Color">Live's colour as 0xRRGGBB, or 0 when this channel has none of its own.</param>
/// <param name="Volume">
/// Live's normalised fader position, 0..1 — not decibels. Unity sits near 0.85, which is where the
/// fader's own travel puts it; see the mixer window for how that becomes a readable scale.
/// </param>
/// <param name="Depth">
/// How many groups this channel sits inside; zero at the top of the set. Carried because a mixer
/// that draws a set built out of groups as one flat row of faders is a worse picture than Live's
/// own — and nesting is not derivable from the order alone.
/// </param>
/// <param name="Display">
/// The reading Live prints under its own fader at this position — "-6.02 dB", "-inf dB". Empty when
/// this Live would not say. It comes from Live rather than from a conversion here because the
/// travel is linear in <paramref name="Volume"/> while the decibels behind it follow a curve
/// Ableton never published: a reimplementation would look authoritative and disagree with the
/// number on the other screen.
/// </param>
public sealed record LiveTrack(
    int Index,
    string Name,
    LiveTrackKind Kind,
    int Color,
    double Volume,
    int Depth = 0,
    string Display = "");
