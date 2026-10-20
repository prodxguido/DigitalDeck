namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// The pair of UDP ports the two halves of the bridge talk on.
/// </summary>
/// <remarks>
/// Configurable because of one specific conflict. AbletonOSC — the project whose address space this
/// one mirrors — listens on 11000 and replies on 11001, and those numbers appear in every tutorial
/// the community has written about it. Live is happy to load both scripts at once, since Control
/// Surface has several slots and they do not compete; the operating system is not, because only one
/// process can bind a port. Whichever script starts second ends up silently deaf.
/// <para>
/// So DigitalDeck steps aside by default rather than asking AbletonOSC's users to move, and the
/// pair stays adjustable for the case where something else on the machine wants these two.
/// </para>
/// </remarks>
/// <param name="ScriptListensOn">Where the Remote Script receives, and the app sends.</param>
/// <param name="ScriptRepliesTo">Where the Remote Script sends, and the app listens.</param>
public readonly record struct AbletonPorts(int ScriptListensOn, int ScriptRepliesTo)
{
    /// <summary>Clear of AbletonOSC's 11000/11001, so an installation of each works untouched.</summary>
    public static AbletonPorts Default => new(11010, 11011);

    /// <summary>The pair AbletonOSC uses, which is the one this must not collide with.</summary>
    public static AbletonPorts AbletonOsc => new(11000, 11001);

    /// <summary>Ports outside this range are either privileged or not assignable.</summary>
    public const int Lowest = 1024;

    public const int Highest = 65535;

    /// <summary>What is wrong with this pair, or null when it is usable.</summary>
    public string? Validate()
    {
        if (ScriptListensOn is < Lowest or > Highest || ScriptRepliesTo is < Lowest or > Highest)
            return $"Los puertos tienen que estar entre {Lowest} y {Highest}.";

        // One socket each: the same number on both sides would have the script trying to bind the
        // port the app already holds.
        if (ScriptListensOn == ScriptRepliesTo)
            return "Los dos puertos tienen que ser distintos.";

        return null;
    }

    /// <summary>True when this pair would fight with AbletonOSC for a socket.</summary>
    public bool CollidesWithAbletonOsc =>
        ScriptListensOn == AbletonOsc.ScriptListensOn
        || ScriptListensOn == AbletonOsc.ScriptRepliesTo
        || ScriptRepliesTo == AbletonOsc.ScriptListensOn
        || ScriptRepliesTo == AbletonOsc.ScriptRepliesTo;

    public override string ToString() => $"{ScriptListensOn} / {ScriptRepliesTo}";
}
