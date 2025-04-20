using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Message;

public class Reply
{

    public MessageType MessageType => MessageType.REPLY;
    public required ushort MessageId { get; init; }


    public required bool Result { get; init; }


    public required ushort RefMessageId { get; init; }
    public required string MessageContent { get; init; }


    public byte[] ToBytes(ushort messageId)
    {

        byte[] contentBytes = Encoding.ASCII.GetBytes(MessageContent);
        var buffer = new byte[1 + 2 + 1 + 2 + contentBytes.Length + 1];

        buffer[0] = (byte)MessageType.REPLY; 

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(1, 2), messageId);

        buffer[3] = (byte)(Result ? 1 : 0);

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), RefMessageId);

    
        contentBytes.CopyTo(buffer.AsSpan(6));

        buffer[^1] = 0;
        return buffer;
    }


    public static Reply FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
            throw new ArgumentException("Invalid REPLY (too short).");

        if (data[0] != (byte)MessageType.REPLY)
            throw new ArgumentException("Message is not a REPLY type.");

 
        ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));

        bool result = (data[3] == 1);

        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));

        ReadOnlySpan<byte> contentPart = data.Slice(6);
        int nullIndex = contentPart.IndexOf((byte)0);
        if (nullIndex < 0)
            throw new ArgumentException("Missing null terminator in REPLY content.");

        string content = Encoding.ASCII.GetString(contentPart.Slice(0, nullIndex));

        return new Reply
        {
            MessageId = msgId,
            Result = result,
            RefMessageId = refId,
            MessageContent = content
        };
    }

}
