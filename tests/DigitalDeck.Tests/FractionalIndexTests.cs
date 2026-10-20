using DigitalDeck.Core.Ordering;
using Xunit;

namespace DigitalDeck.Tests;

public class FractionalIndexTests
{
    [Fact]
    public void FirstKeySitsBetweenTheOpenEnds()
    {
        var key = FractionalIndex.Between(null, null);

        Assert.NotEmpty(key);
    }

    [Fact]
    public void AppendingProducesAscendingKeys()
    {
        var first = FractionalIndex.Last();
        var second = FractionalIndex.Last(first);
        var third = FractionalIndex.Last(second);

        Assert.True(string.CompareOrdinal(first, second) < 0);
        Assert.True(string.CompareOrdinal(second, third) < 0);
    }

    [Fact]
    public void PrependingProducesDescendingKeys()
    {
        var last = FractionalIndex.First();
        var middle = FractionalIndex.First(last);
        var first = FractionalIndex.First(middle);

        Assert.True(string.CompareOrdinal(first, middle) < 0);
        Assert.True(string.CompareOrdinal(middle, last) < 0);
    }

    [Fact]
    public void SequenceIsStrictlyAscending()
    {
        var keys = FractionalIndex.Sequence(50);

        for (var i = 1; i < keys.Count; i++)
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0, $"{keys[i - 1]} should sort before {keys[i]}");
    }

    [Fact]
    public void RepeatedlyInsertingIntoTheSameGapAlwaysFits()
    {
        // The point of fractional keys: there is always room between two neighbours, however many
        // times a user drops something into the same spot.
        var left = FractionalIndex.Between(null, null);
        var right = FractionalIndex.Between(left, null);

        for (var i = 0; i < 200; i++)
        {
            var middle = FractionalIndex.Between(left, right);

            Assert.True(string.CompareOrdinal(left, middle) < 0);
            Assert.True(string.CompareOrdinal(middle, right) < 0);

            right = middle;
        }
    }

    [Fact]
    public void KeysNeverEndInTheLowestDigit()
    {
        var keys = FractionalIndex.Sequence(30);
        var left = keys[0];

        foreach (var key in keys)
        {
            Assert.NotEqual('0', key[^1]);
            left = FractionalIndex.Between(left, null);
            Assert.NotEqual('0', left[^1]);
        }
    }

    [Fact]
    public void RejectsKeysInTheWrongOrder()
    {
        var first = FractionalIndex.Between(null, null);
        var second = FractionalIndex.Between(first, null);

        Assert.Throws<ArgumentException>(() => FractionalIndex.Between(second, first));
    }

    [Fact]
    public void RejectsMalformedKeys()
    {
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("V0", null));
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between(string.Empty, null));
        Assert.Throws<ArgumentException>(() => FractionalIndex.Between("V!", null));
    }
}
