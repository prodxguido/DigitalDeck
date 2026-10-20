namespace DigitalDeck.Core.Voice;

/// <param name="Id">The deck item this phrase should press.</param>
/// <param name="Label">Its name, as written on the tile.</param>
public sealed record VoiceTarget(string Id, string Label);

/// <param name="Confidence">
/// How close the transcript came, from 0 to 1. Reported so the caller can show it rather than only
/// obey it — a run of matches at 0.7 is the sign of a microphone or a name that needs attention.
/// </param>
public sealed record VoiceMatch(VoiceTarget Target, double Confidence);

/// <summary>
/// Turns what the recogniser heard into the button the user meant, or into nothing.
/// </summary>
/// <remarks>
/// The interesting half of this class is when it refuses. A deck is full of names that differ by
/// one word — <c>Reverb</c> and <c>Reverb 2</c>, <c>Bajo</c> and <c>Bajos</c> — and a matcher that
/// always returns its best guess will press the wrong one of a near-tie with total confidence. That
/// is worse than doing nothing: nothing is obvious and repeatable, while the wrong plugin loaded
/// into the wrong track is neither. So a win has to be both good enough on its own and clearly
/// better than the runner-up.
/// </remarks>
public sealed class VoiceCommandMatcher
{
    /// <summary>How close a transcript has to be before it counts as the name at all.</summary>
    /// <remarks>
    /// Low enough to survive a syllable lost to a noisy room, high enough that an unrelated
    /// sentence does not land on a button. Anything below this is reported as no match.
    /// </remarks>
    public const double MinimumConfidence = 0.62;

    /// <summary>How far ahead of the runner-up the winner has to be to count as unambiguous.</summary>
    public const double RequiredMargin = 0.06;

    private readonly List<(VoiceTarget Target, string Normalised)> _targets;

    public VoiceCommandMatcher(IEnumerable<VoiceTarget> targets)
    {
        _targets = targets
            .Select(t => (Target: t, Normalised: VoicePhrase.Normalise(t.Label)))
            .Where(t => t.Normalised.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Every button name in its spoken form, for a recogniser that can be told what to listen for.
    /// </summary>
    /// <remarks>
    /// Handing this to the recogniser as a grammar is the single largest accuracy win available: it
    /// turns "guess which of a hundred thousand Spanish words that was" into "pick one of these
    /// forty or admit you did not hear it". Duplicates are collapsed because two tiles that lead to
    /// the same place are common — the ranking folder is a copy of tiles that live elsewhere.
    /// </remarks>
    /// <summary>
    /// True when nothing here has a name worth listening for.
    /// </summary>
    /// <remarks>
    /// Asked instead of measuring <see cref="Vocabulary"/>, which is never empty now that it
    /// carries the command words — so an empty folder would look like it had forty things to say.
    /// </remarks>
    public bool IsEmpty => _targets.Count == 0;

    public IReadOnlyList<string> Vocabulary =>
        _targets.Select(t => t.Normalised)
            .Concat(VoicePhrase.CommandWords)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The button a transcript names, or null when nothing was close enough or two were equally
    /// close.
    /// </summary>
    public VoiceMatch? Match(string? heard)
    {
        var spoken = VoicePhrase.NormaliseSpoken(heard);

        if (spoken.Length == 0 || _targets.Count == 0) return null;

        VoiceTarget? best = null;
        var bestScore = 0d;
        var runnerUp = 0d;

        foreach (var (target, normalised) in _targets)
        {
            var score = Similarity(spoken, normalised);

            if (score > bestScore)
            {
                runnerUp = bestScore;
                bestScore = score;
                best = target;
            }
            else if (score > runnerUp)
            {
                runnerUp = score;
            }
        }

        if (best is null || bestScore < MinimumConfidence) return null;

        // A near-tie is a question, not an answer. Two tiles with the *same* name are not a tie
        // worth refusing, though — pressing either does the same thing.
        if (bestScore - runnerUp < RequiredMargin && runnerUp >= MinimumConfidence)
        {
            var tied = _targets
                .Where(t => Math.Abs(Similarity(spoken, t.Normalised) - bestScore) < RequiredMargin)
                .Select(t => t.Normalised)
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (tied > 1) return null;
        }

        return new VoiceMatch(best, bestScore);
    }

    /// <summary>
    /// How alike two normalised phrases are, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Edit distance over the whole phrase rather than word-by-word overlap, because the mistakes a
    /// recogniser makes are inside words — <c>serum</c> heard as <c>serun</c>, <c>pro q</c> as
    /// <c>pro cu</c> — and a word-level comparison scores those as total misses.
    /// <para>
    /// A phrase that contains the other outright is treated generously, so saying the full
    /// <c>compresor de bus</c> still finds a tile called <c>compresor</c>.
    /// </para>
    /// </remarks>
    private static double Similarity(string spoken, string target)
    {
        if (spoken == target) return 1;

        var longest = Math.Max(spoken.Length, target.Length);
        if (longest == 0) return 0;

        var edit = 1 - (Distance(spoken, target) / (double)longest);

        if (spoken.Contains(target, StringComparison.Ordinal) ||
            target.Contains(spoken, StringComparison.Ordinal))
        {
            var shortest = Math.Min(spoken.Length, target.Length);

            // Scaled by how much of the longer phrase the shorter one covers, so "compresor" inside
            // "compresor de bus" scores well while "bus" inside it does not.
            var covered = 0.75 + (0.25 * (shortest / (double)longest));

            return Math.Max(edit, covered);
        }

        return edit;
    }

    /// <summary>Levenshtein distance, in two rows rather than a full matrix.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
