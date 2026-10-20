using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// Presses a key combination inside another program.
/// </summary>
/// <remarks>
/// The general form of <see cref="ILiveKeyboard"/>, and the last resort for anything a program has
/// no other way in for. It always needs to know <em>which</em> program: touching a tile gives the
/// deck the keyboard focus, so a chord sent to "whatever is in front" would land on the deck.
/// Bringing the target forward first is the whole difference between a button that works and one
/// that appears to do nothing.
/// </remarks>
public interface IHotkeySender
{
    /// <summary>
    /// Brings <paramref name="program"/> to the front and presses <paramref name="keys"/>.
    /// </summary>
    /// <param name="program">
    /// The start of the process name, as Windows lists it — <c>Ableton Live</c>, <c>chrome</c>,
    /// <c>Photoshop</c>. Matched on a prefix because most of these carry their version in the name.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The program is not running, refused to come forward, or is running with more privilege than
    /// the deck. The message is written to be shown in the status bar as it is.
    /// </exception>
    Task SendAsync(HotkeySpec keys, string program, CancellationToken ct = default);
}
