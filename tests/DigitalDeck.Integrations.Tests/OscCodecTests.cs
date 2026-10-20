using System.Buffers.Binary;
using System.Text;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The wire format, from both ends.
/// </summary>
/// <remarks>
/// Worth testing thoroughly despite being small: every one of these bytes has to agree with a
/// Python file that runs inside another process, in another language, that cannot be stepped
/// through. A padding bug here shows up as a button that mysteriously does nothing.
/// </remarks>
public class OscCodecTests
{
    private static OscMessage RoundTrip(OscMessage message)
    {
        Assert.True(OscCodec.TryDecode(OscCodec.Encode(message), out var decoded, out var error), error);
        Assert.NotNull(decoded);

        return decoded!;
    }

    [Fact]
    public void AMessageWithNoArgumentsSurvives()
    {
        var decoded = RoundTrip(new OscMessage("/live/song/start_playing"));

        Assert.Equal("/live/song/start_playing", decoded.Address);
        Assert.Empty(decoded.Arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void IntegersSurvive(int value)
    {
        Assert.Equal(value, RoundTrip(new OscMessage("/x", value)).GetInt(0));
    }

    [Fact]
    public void FloatsSurvive()
    {
        Assert.Equal(128.5, RoundTrip(new OscMessage("/live/song/set/tempo", 128.5f)).GetDouble(0), 4);
    }

    [Fact]
    public void DoublesSurvive()
    {
        Assert.Equal(0.123456789, RoundTrip(new OscMessage("/x", 0.123456789d)).GetDouble(0), 9);
    }

    [Fact]
    public void StringsSurvive()
    {
        var decoded = RoundTrip(new OscMessage("/x", "Kick", "Bass 808"));

        Assert.Equal("Kick", decoded.GetString(0));
        Assert.Equal("Bass 808", decoded.GetString(1));
    }

    [Fact]
    public void NonAsciiTextSurvives()
    {
        // Track names in a Spanish-language set routinely have accents in them.
        Assert.Equal("Percusión", RoundTrip(new OscMessage("/x", "Percusión")).GetString(0));
    }

    [Fact]
    public void BooleansTravelInTheTypeTagWithNoPayload()
    {
        var encoded = OscCodec.Encode(new OscMessage("/x", true, false));

        // "/x" plus ",TF", each padded to four bytes, and nothing else.
        Assert.Equal(8, encoded.Length);

        var decoded = RoundTrip(new OscMessage("/x", true, false));
        Assert.True(decoded.GetBool(0));
        Assert.False(decoded.GetBool(1));
    }

    [Fact]
    public void BlobsSurvive()
    {
        var decoded = RoundTrip(new OscMessage("/x", new byte[] { 1, 2, 3 }));

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(decoded.Arguments[0]));
    }

    [Fact]
    public void MixedArgumentsKeepTheirOrder()
    {
        var decoded = RoundTrip(new OscMessage("/live/track/set/volume", 2, 0.5f, "Bass", true));

        Assert.Equal(2, decoded.GetInt(0));
        Assert.Equal(0.5, decoded.GetDouble(1), 4);
        Assert.Equal("Bass", decoded.GetString(2));
        Assert.True(decoded.GetBool(3));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    [InlineData("")]
    public void EveryBlockLandsOnAFourByteBoundary(string text)
    {
        // Lengths around a multiple of four are where a naive implementation goes wrong: with the
        // null terminator these need 0, 3 and 2 bytes of padding.
        var encoded = OscCodec.Encode(new OscMessage("/x", text));

        Assert.Equal(0, encoded.Length % 4);
        Assert.Equal(text, RoundTrip(new OscMessage("/x", text)).GetString(0));
    }

    [Theory]
    [InlineData("/a")]
    [InlineData("/ab")]
    [InlineData("/abc")]
    [InlineData("/abcd")]
    [InlineData("/abcde")]
    public void AddressesOfAwkwardLengthSurvive(string address)
    {
        Assert.Equal(address, RoundTrip(new OscMessage(address)).Address);
    }

    // ---------- Malformed input ----------
    //
    // These arrive over UDP from another process. A bad datagram is a normal event, not a bug, and
    // it has to come back as false rather than as an exception in the receive loop.

    [Fact]
    public void AnEmptyDatagramIsRejected()
    {
        Assert.False(OscCodec.TryDecode(Array.Empty<byte>(), out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void BundlesAreRejectedRatherThanHalfParsed()
    {
        var bundle = Encoding.ASCII.GetBytes("#bundle\0").Concat(new byte[8]).ToArray();

        Assert.False(OscCodec.TryDecode(bundle, out _, out _));
    }

    [Fact]
    public void AnAddressWithoutALeadingSlashIsRejected()
    {
        Assert.False(OscCodec.TryDecode(Encoding.ASCII.GetBytes("live\0\0\0\0"), out _, out _));
    }

    [Fact]
    public void AnUnterminatedStringIsRejected()
    {
        Assert.False(OscCodec.TryDecode(Encoding.ASCII.GetBytes("/no-null-here"), out _, out _));
    }

    [Fact]
    public void ATruncatedArgumentIsRejected()
    {
        // Claims an int32 but carries two bytes of it.
        var data = Encoding.ASCII.GetBytes("/x\0\0,i\0\0").Concat(new byte[] { 0, 0 }).ToArray();

        Assert.False(OscCodec.TryDecode(data, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void AnUnknownTypeTagIsRejected()
    {
        Assert.False(OscCodec.TryDecode(Encoding.ASCII.GetBytes("/x\0\0,Z\0\0"), out _, out _));
    }

    [Fact]
    public void ABlobLengthThatWouldOverrunIsRejected()
    {
        var data = new byte[12];
        Encoding.ASCII.GetBytes("/x\0\0,b\0\0").CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), int.MaxValue);

        Assert.False(OscCodec.TryDecode(data, out _, out _));
    }

    [Fact]
    public void ANegativeBlobLengthIsRejected()
    {
        var data = new byte[12];
        Encoding.ASCII.GetBytes("/x\0\0,b\0\0").CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), -1);

        Assert.False(OscCodec.TryDecode(data, out _, out _));
    }

    [Fact]
    public void MissingTypeTagsAreTreatedAsNoArguments()
    {
        // Some senders leave them out. Accepting it costs nothing and avoids a dead button.
        Assert.True(OscCodec.TryDecode(Encoding.ASCII.GetBytes("/live/song/undo\0"), out var message, out _));
        Assert.Equal("/live/song/undo", message!.Address);
        Assert.Empty(message.Arguments);
    }

    [Fact]
    public void AnAddressMustBeAbsoluteWhenBuildingAMessage()
    {
        Assert.Throws<ArgumentException>(() => new OscMessage("live/song"));
    }

    [Fact]
    public void EncodingAnUnsupportedTypeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => OscCodec.Encode(new OscMessage("/x", new object())));
    }

    // ---------- Accessors ----------

    [Fact]
    public void AccessorsFallBackInsteadOfThrowingOnAnUnexpectedReply()
    {
        // The script is a separate program that can be upgraded independently; a reply of the wrong
        // shape must not take down whatever is reading it.
        var message = new OscMessage("/x", "text");

        Assert.Equal(-1, message.GetInt(0, -1));
        Assert.Equal(0, message.GetDouble(5));
        Assert.Null(message.GetString(9));
        Assert.False(message.GetBool(3));
    }

    [Fact]
    public void ToStringIsReadableForALog()
    {
        Assert.Equal("/live/track/set/volume 1 \"Bass\"",
                     new OscMessage("/live/track/set/volume", 1, "Bass").ToString());
    }
}
