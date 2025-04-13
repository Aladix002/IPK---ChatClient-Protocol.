using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using Message;


public class Join : IMessage
{
    public MessageType MessageType => MessageType.JOIN;

    public required string ChannelId { get; init; }
    public required string DisplayName { get; init; }

    public static string ToTcpString(Join join)
    {
        if (join.ChannelId.Length > 20 || join.DisplayName.Length > 20)
            throw new ArgumentException("ChannelId and DisplayName must be at most 20 characters.");

        if (!Regex.IsMatch(join.ChannelId, @"^[a-zA-Z0-9_.-]+$"))
            throw new ArgumentException("ChannelId can contain only a-z, A-Z, 0-9, '-', '_' or '.'");

        if (!Regex.IsMatch(join.DisplayName, @"^[\x20-\x7E]*$"))
            throw new ArgumentException("DisplayName must contain only printable ASCII characters");

        return $"JOIN {join.ChannelId} AS {join.DisplayName}\r\n";
    }

    public byte[] ToBytes(ushort id)
{
    byte[] channelIdBytes = Encoding.UTF8.GetBytes(ChannelId);
    byte[] displayNameBytes = Encoding.UTF8.GetBytes(DisplayName);

    byte[] result = new byte[1 + 2 + channelIdBytes.Length + 1 + displayNameBytes.Length + 1];

    int offset = 0;

    // MessageType (1 byte)
    result[offset++] = (byte)MessageType;

    // MessageId (2 bytes) - BIG ENDIAN
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset, 2), id);
    offset += 2;

    // ChannelId
    Array.Copy(channelIdBytes, 0, result, offset, channelIdBytes.Length);
    offset += channelIdBytes.Length;
    result[offset++] = 0;

    // DisplayName
    Array.Copy(displayNameBytes, 0, result, offset, displayNameBytes.Length);
    offset += displayNameBytes.Length;
    result[offset] = 0;

    return result;
}


    public static Join FromTcpString(string[] parts)
    {
        if (parts.Length != 4 || parts[0] != "JOIN" || parts[2] != "AS")
            throw new ArgumentException("Invalid JOIN message format");

        return new Join
        {
            ChannelId = parts[1],
            DisplayName = parts[3]
        };
    }
}
