using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using Message;


public class Join
{
    public MessageType MessageType => MessageType.JOIN;

    public required string ChannelId { get; init; }
    public required string DisplayName { get; init; }


    public byte[] ToBytes(ushort id)
{
    byte[] channelIdBytes = Encoding.UTF8.GetBytes(ChannelId);
    byte[] displayNameBytes = Encoding.UTF8.GetBytes(DisplayName);

    byte[] result = new byte[1 + 2 + channelIdBytes.Length + 1 + displayNameBytes.Length + 1];

    int offset = 0;

  
    result[offset++] = (byte)MessageType;

   
    System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset, 2), id);
    offset += 2;


    Array.Copy(channelIdBytes, 0, result, offset, channelIdBytes.Length);
    offset += channelIdBytes.Length;
    result[offset++] = 0;

 
    Array.Copy(displayNameBytes, 0, result, offset, displayNameBytes.Length);
    offset += displayNameBytes.Length;
    result[offset] = 0;

    return result;
}

}
