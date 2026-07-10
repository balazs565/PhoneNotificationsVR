using System.Buffers.Binary;
using System.Text;

namespace PhoneNotificationsVR.Ancs;

/// <summary>The 8-byte packet delivered on the Notification Source characteristic.</summary>
public readonly record struct AncsNotificationSourcePacket(
    AncsEventId EventId,
    AncsEventFlags Flags,
    byte CategoryId,
    byte CategoryCount,
    uint NotificationUid)
{
    public static AncsNotificationSourcePacket Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException($"Notification Source packet too short ({data.Length} bytes).");

        return new AncsNotificationSourcePacket(
            (AncsEventId)data[0],
            (AncsEventFlags)data[1],
            data[2],
            data[3],
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)));
    }
}

/// <summary>Builds Control Point command payloads.</summary>
public static class AncsRequestBuilder
{
    /// <summary>
    /// Build a GetNotificationAttributes command requesting app id, title, subtitle, message and date.
    /// The 2-byte max-length is required for title/subtitle/message per the spec.
    /// </summary>
    public static byte[] GetNotificationAttributes(uint uid, ushort maxTextLength = 512)
    {
        var buf = new List<byte>(24) { (byte)AncsCommandId.GetNotificationAttributes };

        Span<byte> uidBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(uidBytes, uid);
        buf.AddRange(uidBytes.ToArray());

        buf.Add((byte)AncsNotificationAttributeId.AppIdentifier);

        AddLengthPrefixed(buf, AncsNotificationAttributeId.Title, maxTextLength);
        AddLengthPrefixed(buf, AncsNotificationAttributeId.Subtitle, maxTextLength);
        AddLengthPrefixed(buf, AncsNotificationAttributeId.Message, maxTextLength);

        buf.Add((byte)AncsNotificationAttributeId.Date);

        return buf.ToArray();

        static void AddLengthPrefixed(List<byte> b, AncsNotificationAttributeId id, ushort len)
        {
            b.Add((byte)id);
            Span<byte> l = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(l, len);
            b.Add(l[0]);
            b.Add(l[1]);
        }
    }

    /// <summary>Build a GetAppAttributes command asking for the app's display name.</summary>
    public static byte[] GetAppAttributes(string appIdentifier)
    {
        var buf = new List<byte> { (byte)AncsCommandId.GetAppAttributes };
        buf.AddRange(Encoding.UTF8.GetBytes(appIdentifier));
        buf.Add(0); // NUL-terminated app identifier string
        buf.Add((byte)AncsAppAttributeId.DisplayName);
        return buf.ToArray();
    }
}

/// <summary>Parsed notification attribute set returned on the Data Source characteristic.</summary>
public sealed class AncsNotificationAttributes
{
    public uint Uid { get; init; }
    public string AppIdentifier { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
}

/// <summary>
/// Reassembles Data Source fragments into complete attribute responses.
/// GATT notifications are capped at the ATT MTU, so a long message arrives in several chunks.
/// We buffer bytes and only emit once every declared attribute has been fully received.
/// </summary>
public sealed class AncsDataSourceAssembler
{
    private readonly List<byte> _buffer = new();

    /// <summary>Feed a Data Source fragment. Returns a completed attribute set if the buffer now holds one.</summary>
    public AncsNotificationAttributes? Push(ReadOnlySpan<byte> fragment)
    {
        _buffer.AddRange(fragment.ToArray());
        return TryParse();
    }

    public void Reset() => _buffer.Clear();

    private AncsNotificationAttributes? TryParse()
    {
        // Layout: CommandID(1) UID(4) then repeated { AttrID(1) Len(2) Value(Len) }.
        // We only parse GetNotificationAttributes (command 0) responses here.
        var span = _buffer.ToArray().AsSpan();
        if (span.Length < 5) return null;
        if (span[0] != (byte)AncsCommandId.GetNotificationAttributes) { _buffer.Clear(); return null; }

        uint uid = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(1, 4));
        int offset = 5;

        var result = new AncsNotificationAttributes { Uid = uid };
        int attributesParsed = 0;

        while (offset + 3 <= span.Length)
        {
            var attrId = (AncsNotificationAttributeId)span[offset];
            ushort len = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 1, 2));
            int valueStart = offset + 3;

            // Not all bytes arrived yet – wait for the next fragment.
            if (valueStart + len > span.Length) return null;

            var value = Encoding.UTF8.GetString(span.Slice(valueStart, len));
            switch (attrId)
            {
                case AncsNotificationAttributeId.AppIdentifier: result.AppIdentifier = value; break;
                case AncsNotificationAttributeId.Title: result.Title = value; break;
                case AncsNotificationAttributeId.Subtitle: result.Subtitle = value; break;
                case AncsNotificationAttributeId.Message: result.Message = value; break;
                case AncsNotificationAttributeId.Date: result.Date = ParseAncsDate(value); break;
            }

            offset = valueStart + len;
            attributesParsed++;

            // We requested 5 attributes (appId, title, subtitle, message, date). Once we have them all,
            // the response is complete regardless of any trailing bytes.
            if (attributesParsed >= 5) break;
        }

        if (attributesParsed >= 5)
        {
            _buffer.Clear();
            return result;
        }
        return null;
    }

    /// <summary>ANCS dates are formatted as "yyyyMMdd'T'HHmmss" in the phone's local time.</summary>
    private static DateTimeOffset? ParseAncsDate(string value)
    {
        if (DateTimeOffset.TryParseExact(value, "yyyyMMdd'T'HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
            return dt;
        return null;
    }
}
