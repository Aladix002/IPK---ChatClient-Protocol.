using System.Text;
using System.Text.RegularExpressions;
using Message;

namespace Client.Messages;

public class Join : IMessage
{
    public MessageType MessageType => MessageType.JOIN;

    public required string ChannelId { get; init; }
    public required string DisplayName { get; init; }

    public string ToTcpString()
    {
        if (ChannelId.Length > 20 || DisplayName.Length > 20)
            throw new ArgumentException("ChannelId and DisplayName must be at most 20 characters.");

        if (!Regex.IsMatch(ChannelId, @"^[a-zA-Z0-9_.-]+$"))
            throw new ArgumentException("ChannelId can contain only a-z, A-Z, 0-9, '-', '_' or '.'");

        if (!Regex.IsMatch(DisplayName, @"^[\x20-\x7E]*$"))
            throw new ArgumentException("DisplayName must contain only printable ASCII characters");

        return $"JOIN {ChannelId} AS {DisplayName}\r\n";
    }

    public byte[] ToBytes(ushort messageId)
    {
        byte[] channelBytes = Encoding.ASCII.GetBytes(ChannelId);
        byte[] displayBytes = Encoding.ASCII.GetBytes(DisplayName);

        byte[] result = new byte[1 + 2 + channelBytes.Length + 1 + displayBytes.Length + 1];
        result[0] = (byte)MessageType;

        ushort networkId = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(messageId);
        byte[] idBytes = BitConverter.GetBytes(networkId);
        Array.Copy(idBytes, 0, result, 1, 2);

        int offset = 3;
        Array.Copy(channelBytes, 0, result, offset, channelBytes.Length);
        offset += channelBytes.Length;
        result[offset++] = 0;

        Array.Copy(displayBytes, 0, result, offset, displayBytes.Length);
        offset += displayBytes.Length;
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
