using System;
using System.Buffers.Binary;

namespace Message;

public class Confirm
{
    public MessageType MessageType => MessageType.CONFIRM;
   
    public required ushort RefMessageId { get; init; }

    
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

        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
        return new Confirm { RefMessageId = refId };
    }
}


