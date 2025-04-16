using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Binary;

namespace Message;

public class Err
{
    public MessageType MessageType => MessageType.ERR;
    public ushort MessageId { get; set; } 
    public required string DisplayName { get; init; }
    public required string MessageContents { get; init; }


    public byte[] ToBytes(ushort id)
    {
        MessageId = id;
        var nameBytes = Encoding.UTF8.GetBytes(DisplayName);
        var contentBytes = Encoding.UTF8.GetBytes(MessageContents);
        var result = new byte[1 + 2 + nameBytes.Length + 1 + contentBytes.Length + 1];

        result[0] = (byte)MessageType;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), id);

        int offset = 3;
        nameBytes.CopyTo(result.AsSpan(offset));
        offset += nameBytes.Length;
        result[offset++] = 0;

        contentBytes.CopyTo(result.AsSpan(offset));
        offset += contentBytes.Length;
        result[offset] = 0;

        return result;
    }

    public static Err FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != (byte)MessageType.ERR)
            throw new ArgumentException("Invalid ERR byte data.");

        var id = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
        int offset = 3;

        int nameEnd = data.Slice(offset).IndexOf((byte)0);
        if (nameEnd == -1) throw new ArgumentException("Missing null terminator after DisplayName.");

        string displayName = Encoding.UTF8.GetString(data.Slice(offset, nameEnd));
        offset += nameEnd + 1;

        int contentEnd = data.Slice(offset).IndexOf((byte)0);
        if (contentEnd == -1) throw new ArgumentException("Missing null terminator after MessageContent.");

        string message = Encoding.UTF8.GetString(data.Slice(offset, contentEnd));

        return new Err
        {
            MessageId = id,
            DisplayName = displayName,
            MessageContents = message
        };
    }
}

