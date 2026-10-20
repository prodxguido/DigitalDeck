namespace DigitalDeck.Core.Ordering;

/// <summary>
/// Lexicographic ("fractional") ordering keys. Reordering an item by drag and drop becomes a
/// single row update — compute a key that sorts between the two neighbours — instead of
/// renumbering every sibling. Keys are compared with ordinal string comparison.
/// </summary>
/// <remarks>
/// Keys never end in the lowest digit ('0'), for the same reason decimals are not written with
/// trailing zeros: it would make two different strings represent the same position and break the
/// "always room in between" guarantee.
/// </remarks>
public static class FractionalIndex
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Key that sorts strictly between <paramref name="left"/> and <paramref name="right"/>.</summary>
    /// <param name="left">Preceding sibling's key, or null when inserting at the start.</param>
    /// <param name="right">Following sibling's key, or null when appending at the end.</param>
    public static string Between(string? left, string? right)
    {
        Validate(left, nameof(left));
        Validate(right, nameof(right));

        if (left is not null && right is not null && string.CompareOrdinal(left, right) >= 0)
            throw new ArgumentException($"Key '{left}' must sort before key '{right}'.", nameof(left));

        return Midpoint(left ?? string.Empty, right);
    }

    /// <summary>Key for a new first item, given the key currently in first position.</summary>
    public static string First(string? currentFirst = null) => Between(null, currentFirst);

    /// <summary>Key for a new last item, given the key currently in last position.</summary>
    public static string Last(string? currentLast = null) => Between(currentLast, null);

    /// <summary>Keys for <paramref name="count"/> consecutive items appended after <paramref name="after"/>.</summary>
    public static IReadOnlyList<string> Sequence(int count, string? after = null)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        var keys = new List<string>(count);
        var previous = after;
        for (var i = 0; i < count; i++)
        {
            previous = Between(previous, null);
            keys.Add(previous);
        }
        return keys;
    }

    private static void Validate(string? key, string parameterName)
    {
        if (key is null) return;

        if (key.Length == 0)
            throw new ArgumentException("Key must not be empty; use null for an open end.", parameterName);

        if (key[^1] == Digits[0])
            throw new ArgumentException($"Key '{key}' must not end with '{Digits[0]}'.", parameterName);

        foreach (var c in key)
        {
            if (Digits.IndexOf(c) < 0)
                throw new ArgumentException($"Key '{key}' contains character '{c}', which is not a valid digit.", parameterName);
        }
    }

    private static string Midpoint(string left, string? right)
    {
        if (right is not null)
        {
            // The midpoint only depends on where the two keys diverge, so carry the shared
            // prefix through untouched and recurse on the remainder.
            var shared = 0;
            while (shared < right.Length && (shared < left.Length ? left[shared] : Digits[0]) == right[shared])
                shared++;

            if (shared > 0)
            {
                var restLeft = shared < left.Length ? left[shared..] : string.Empty;
                return string.Concat(right[..shared], Midpoint(restLeft, right[shared..]));
            }
        }

        var digitLeft = left.Length > 0 ? Digits.IndexOf(left[0]) : 0;
        var digitRight = right is { Length: > 0 } ? Digits.IndexOf(right[0]) : Digits.Length;

        if (digitRight - digitLeft > 1)
        {
            var middle = (int)Math.Round((digitLeft + digitRight) / 2.0, MidpointRounding.AwayFromZero);
            return Digits[middle].ToString();
        }

        // The leading digits are adjacent, so there is no room at this position: borrow the
        // right key's leading digit if it has more to give, otherwise grow the left key.
        if (right is { Length: > 1 })
            return right[..1];

        return Digits[digitLeft] + Midpoint(left.Length > 0 ? left[1..] : string.Empty, null);
    }
}
