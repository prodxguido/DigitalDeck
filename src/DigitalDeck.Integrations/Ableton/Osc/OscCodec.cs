using System.Buffers.Binary;
using System.Text;

namespace DigitalDeck.Integrations.Ableton.Osc;

/// <summary>
/// OSC 1.0 on the wire — the exact mirror of <c>osc.py</c> in the Remote Script.
/// </summary>
/// <remarks>
/// Written by hand rather than taken from a package. The format is a few hundred bytes of rules
/// (address, type tags, everything padded to four bytes, big endian), only a handful of types are
/// ever exchanged, and having both ends of the bridge readable side by side is worth more here
/// than a dependency would be.
/// <para>
/// Decoding assumes hostile input. The datagram arrives over UDP, so a truncated or malformed one
/// is a normal event, not a bug — every read is bounds checked and failure comes back as false.
/// </para>
/// </remarks>
public static class OscCodec
{
    private const int Alignment = 4;

    /// <summary>Refuses anything larger, so a bad length prefix cannot make us allocate wildly.</summary>
    private const int MaxBlobLength = 1 << 20;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] Encode(OscMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tags = new StringBuilder(",");
        var body = new List<byte>(64);

        foreach (var argument in message.Arguments)
        {
            switch (argument)
            {
                case bool flag:
                    // True and False are carried by the tag alone; there is no payload.
                    tags.Append(flag ? 'T' : 'F');
                    break;

                case int value:
                    tags.Append('i');
                    AppendInt32(body, value);
                    break;

                case long value:
                    // No int64 in the subset the script understands, so widen instead of wrapping.
                    tags.Append('d');
                    AppendDouble(body, value);
                    break;

                case float value:
                    tags.Append('f');
                    AppendFloat(body, value);
                    break;

                case double value:
                    tags.Append('d');
                    AppendDouble(body, value);
                    break;

                case string text:
                    tags.Append('s');
                    body.AddRange(EncodeString(text));
                    break;

                case byte[] blob:
                    tags.Append('b');
                    AppendInt32(body, blob.Length);
                    body.AddRange(blob);
                    body.AddRange(new byte[PaddingFor(blob.Length)]);
                    break;

                case null:
                    tags.Append('N');
                    break;

                default:
                    throw new ArgumentException(
                        $"No hay forma de mandar un {argument.GetType().Name} por OSC.",
                        nameof(message));
            }
        }

        var address = EncodeString(message.Address);
        var typeTags = EncodeString(tags.ToString());

        var buffer = new byte[address.Length + typeTags.Length + body.Count];
        address.CopyTo(buffer, 0);
        typeTags.CopyTo(buffer, address.Length);
        body.CopyTo(buffer, address.Length + typeTags.Length);

        return buffer;
    }

    /// <summary>
    /// Parses one datagram. Returns false — never throws — for anything that is not a well formed
    /// message, including bundles, which this bridge never uses.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out OscMessage? message, out string? error)
    {
        message = null;

        if (data.IsEmpty)
        {
            error = "el datagrama está vacío";
            return false;
        }

        if (data[0] == (byte)'#')
        {
            error = "los bundles no están soportados";
            return false;
        }

        var offset = 0;

        if (!TryReadString(data, ref offset, out var address))
        {
            error = "la dirección está mal formada";
            return false;
        }

        if (address.Length == 0 || address[0] != '/')
        {
            error = "la dirección no empieza con '/'";
            return false;
        }

        // A message with no type tag string at all: some senders omit it. Treated as no arguments,
        // which costs nothing and keeps a button working instead of failing on a technicality.
        if (offset >= data.Length)
        {
            message = new OscMessage(address);
            error = null;
            return true;
        }

        if (!TryReadString(data, ref offset, out var tags) || tags.Length == 0 || tags[0] != ',')
        {
            error = "los type tags están mal formados";
            return false;
        }

        var arguments = new List<object?>(tags.Length - 1);

        for (var i = 1; i < tags.Length; i++)
        {
            switch (tags[i])
            {
                case 'T':
                    arguments.Add(true);
                    break;

                case 'F':
                    arguments.Add(false);
                    break;

                case 'N':
                    arguments.Add(null);
                    break;

                case 'i':
                    if (!TryTake(data, ref offset, 4, out var intBytes)) goto truncated;
                    arguments.Add(BinaryPrimitives.ReadInt32BigEndian(intBytes));
                    break;

                case 'f':
                    if (!TryTake(data, ref offset, 4, out var floatBytes)) goto truncated;
                    arguments.Add(BinaryPrimitives.ReadSingleBigEndian(floatBytes));
                    break;

                case 'd':
                    if (!TryTake(data, ref offset, 8, out var doubleBytes)) goto truncated;
                    arguments.Add(BinaryPrimitives.ReadDoubleBigEndian(doubleBytes));
                    break;

                case 's':
                    if (!TryReadString(data, ref offset, out var text))
                    {
                        error = "un argumento de texto está mal formado";
                        return false;
                    }

                    arguments.Add(text);
                    break;

                case 'b':
                    if (!TryTake(data, ref offset, 4, out var lengthBytes)) goto truncated;

                    var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
                    if (length < 0 || length > MaxBlobLength)
                    {
                        error = "el largo del blob no es válido";
                        return false;
                    }

                    if (!TryTake(data, ref offset, length + PaddingFor(length), out var blobBytes))
                        goto truncated;

                    arguments.Add(blobBytes[..length].ToArray());
                    break;

                default:
                    error = $"type tag '{tags[i]}' no soportado";
                    return false;
            }
        }

        message = new OscMessage(address, arguments.ToArray());
        error = null;
        return true;

    truncated:
        error = "el mensaje viene cortado";
        return false;
    }

    // ---------- Building blocks ----------

    /// <summary>Bytes of padding needed to land on the next four byte boundary.</summary>
    private static int PaddingFor(int length)
    {
        var remainder = length % Alignment;
        return remainder == 0 ? 0 : Alignment - remainder;
    }

    private static byte[] EncodeString(string value)
    {
        var raw = Utf8.GetByteCount(value);
        var total = raw + 1; // The null terminator counts towards the padding.
        var buffer = new byte[total + PaddingFor(total)];

        Utf8.GetBytes(value, buffer);
        return buffer;
    }

    private static bool TryReadString(ReadOnlySpan<byte> data, ref int offset, out string value)
    {
        value = string.Empty;

        if (offset >= data.Length) return false;

        var rest = data[offset..];
        var terminator = rest.IndexOf((byte)0);

        if (terminator < 0) return false;

        value = Utf8.GetString(rest[..terminator]);

        var consumed = terminator + 1;
        offset += consumed + PaddingFor(consumed);

        // The padding itself may run past the end on a truncated datagram; that is still a valid
        // read of the string, and the next TryTake is what will notice there is nothing left.
        return true;
    }

    private static bool TryTake(ReadOnlySpan<byte> data, ref int offset, int size, out ReadOnlySpan<byte> taken)
    {
        if (offset < 0 || size < 0 || offset + size > data.Length)
        {
            taken = default;
            return false;
        }

        taken = data.Slice(offset, size);
        offset += size;
        return true;
    }

    private static void AppendInt32(List<byte> body, int value)
    {
        var scratch = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(scratch, value);
        body.AddRange(scratch);
    }

    private static void AppendFloat(List<byte> body, float value)
    {
        var scratch = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(scratch, value);
        body.AddRange(scratch);
    }

    private static void AppendDouble(List<byte> body, double value)
    {
        var scratch = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(scratch, value);
        body.AddRange(scratch);
    }
}
