using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Message;

public class UdpMessage
{
    public MessageType Type { get; set; }
    public ushort MessageId { get; set; }

    public string? Username { get; set; }
    public string? Secret { get; set; }
    public string? DisplayName { get; set; }
    public string? ChannelId { get; set; }
    public string? MessageContents { get; set; }
    public ushort? RefMessageId { get; set; }
    public bool? Result { get; set; }

    //vyrvori spravu na byty
    public byte[] ToBytes(ushort messageId)
    {
        MessageId = messageId;
        var buffer = new List<byte> { (byte)Type };

        // 2 byty pre message id 
        buffer.AddRange(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)messageId)));
        switch (Type)
        {
            case MessageType.AUTH:
                AddZ(buffer, Username);
                AddZ(buffer, DisplayName);
                AddZ(buffer, Secret);
                break;

            case MessageType.JOIN:
                AddZ(buffer, ChannelId);
                AddZ(buffer, DisplayName);
                break;

            case MessageType.MSG:
                AddZ(buffer, DisplayName);
                AddZ(buffer, MessageContents);
                break;

            case MessageType.BYE:
                AddZ(buffer, DisplayName);
                break;

            case MessageType.CONFIRM:
                buffer.AddRange(ToUInt16Bytes(RefMessageId ?? 0));
                break;

            case MessageType.REPLY:
                buffer.Add((byte)(Result == true ? 1 : 0));
                buffer.AddRange(ToUInt16Bytes(RefMessageId ?? 0));
                AddZ(buffer, MessageContents);
                break;

            case MessageType.ERR:
                AddZ(buffer, DisplayName);
                AddZ(buffer, MessageContents);
                break;

            case MessageType.PING:
                break;
        }

        return buffer.ToArray();
    }

    //parsovanie spravy z bytov
    public static UdpMessage ParseUdp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
            throw new ArgumentException("Too short");

        var msg = new UdpMessage
        {
            Type = (MessageType)data[0],
            MessageId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)) 
            };

        int offset = 3;

        switch (msg.Type)
        {
            case MessageType.AUTH:
                msg.Username = ReadZString(data, ref offset);
                msg.DisplayName = ReadZString(data, ref offset);
                msg.Secret = ReadZString(data, ref offset);
                break;

            case MessageType.JOIN:
                msg.ChannelId = ReadZString(data, ref offset);
                msg.DisplayName = ReadZString(data, ref offset);
                break;

            case MessageType.MSG:
                msg.DisplayName = ReadZString(data, ref offset);
                msg.MessageContents = ReadZString(data, ref offset);
                break;

            case MessageType.BYE:
                msg.DisplayName = ReadZString(data, ref offset);
                break;

            case MessageType.CONFIRM:
                msg.RefMessageId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));
                break;

            case MessageType.REPLY:
                msg.Result = data[offset++] == 1;
                msg.RefMessageId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
                offset += 2;
                msg.MessageContents = ReadZString(data, ref offset);
                break;

            case MessageType.ERR:
                msg.DisplayName = ReadZString(data, ref offset);
                msg.MessageContents = ReadZString(data, ref offset);
                break;
        }

        return msg;
    }

    private static string ReadZString(ReadOnlySpan<byte> data, ref int offset)
    {
        int end = data.Slice(offset).IndexOf((byte)0); //najde null terminator
        if (end < 0) throw new ArgumentException("Missing null terminator");

        string s = Encoding.UTF8.GetString(data.Slice(offset, end)); // https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getstring
        offset += end + 1;
        return s;
    }

    //prida string do bufferu s null terminatorom
    private static void AddZ(List<byte> buffer, string? s)
    {
        if (s == null) return;
        buffer.AddRange(Encoding.UTF8.GetBytes(s));
        buffer.Add(0);
    }

    //zapisuje ushort vo formate bigendian na message a ref id 
    private static byte[] ToUInt16Bytes(ushort value)
    {
        byte[] b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        return b;
    }
}

