using System;
using System.Buffers.Binary;

namespace Message;

public class Confirm
{
    public MessageType MessageType => MessageType.CONFIRM;
    
    // Which message ID we're confirming
    public required ushort RefMessageId { get; init; }

    // We ignore the 'messageId' because, in this protocol, a CONFIRM
    // doesn't get its own new ID. It's purely "Confirming" an existing ID.
    public byte[] ToBytes(ushort ignored)
    {
        var result = new byte[3];
        result[0] = (byte)MessageType.CONFIRM; 
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), RefMessageId);
        return result;
    }

    public static Confirm FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)MessageType.CONFIRM)
            throw new ArgumentException("Invalid CONFIRM message");

        // The 2-byte RefMessageId is in bytes [1..2], big-endian
        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
        return new Confirm { RefMessageId = refId };
    }
}


