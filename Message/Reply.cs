using System.Net;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Message;

public class Reply : IMessage
{
    public MessageType MessageType => MessageType.REPLY;
    public required bool Result { get; init; }
    public required ushort RefMessageId { get; init; }
    public required string MessageContent { get; init; }

    public byte[] ToBytes(ushort messageId)
    {
        byte[] contentBytes = Encoding.ASCII.GetBytes(MessageContent);
        byte[] result = new byte[1 + 2 + 1 + 2 + contentBytes.Length + 1];

        result[0] = (byte)MessageType.REPLY;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), messageId);
        result[3] = (byte)(Result ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4, 2), RefMessageId);
        contentBytes.CopyTo(result.AsSpan(6));
        result[^1] = 0;

        return result;
    }

    public static Reply FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6 || data[0] != (byte)MessageType.REPLY)
            throw new ArgumentException("Invalid Reply message");

        bool result = data[3] == 1;
        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
        int contentStart = 6;
        int nullIndex = data.Slice(contentStart).IndexOf((byte)0);
        if (nullIndex == -1)
            throw new ArgumentException("Missing null terminator in message content");

        string content = Encoding.ASCII.GetString(data.Slice(contentStart, nullIndex));

        return new Reply
        {
            Result = result,
            RefMessageId = refId,
            MessageContent = content
        };
    }

    public static Reply FromTcpString(string[] words)
    {
        if (words.Length != 4 || words[0] != "REPLY")
            throw new ArgumentException("Invalid REPLY format");

        bool result = words[1] switch
        {
            "OK" => true,
            "NOK" => false,
            _ => throw new ArgumentException("Invalid REPLY result")
        };

        if (words[2] != "IS")
            throw new ArgumentException("Invalid REPLY keyword");

        string content = words[3];
        if (content.Length > 1400 || !Regex.IsMatch(content, @"^[\x20-\x7E\s]*$"))
            throw new ArgumentException("Invalid REPLY content");

        return new Reply
        {
            Result = result,
            RefMessageId = 0, 
            MessageContent = content
        };
    }
}