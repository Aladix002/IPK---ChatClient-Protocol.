using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Message;

public class Reply : IMessage
{
    // Protocol says 0x01 in the first byte means REPLY
    public MessageType MessageType => MessageType.REPLY;

    // The ID of this REPLY message itself
    public required ushort MessageId { get; init; }

    // Indicates success (true => 1) or failure (false => 0)
    public required bool Result { get; init; }

    // The original message ID we are replying to
    public required ushort RefMessageId { get; init; }

    // Human-readable text or data
    public required string MessageContent { get; init; }

    /// <summary>
    /// Convert this Reply into its UDP byte form:
    /// [0]           = (byte)MessageType.REPLY = 0x01
    /// [1..2]        = big-endian MessageId
    /// [3]           = Result (0 or 1)
    /// [4..5]        = big-endian RefMessageId
    /// [6..N-2]      = ASCII-encoded MessageContent
    /// [N-1]         = 0 terminator
    /// </summary>
    public byte[] ToBytes(ushort messageId)
    {
        // We'll overwrite messageId as the actual MessageId in the packet
        byte[] contentBytes = Encoding.ASCII.GetBytes(MessageContent);
        var buffer = new byte[1 + 2 + 1 + 2 + contentBytes.Length + 1];

        // 0) Message Type
        buffer[0] = (byte)MessageType.REPLY; // 0x01

        // 1..2) Our new message ID in big-endian
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(1, 2), messageId);

        // 3) Result (1 = success, 0 = failure)
        buffer[3] = (byte)(Result ? 1 : 0);

        // 4..5) Reference ID (which message are we replying to)
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), RefMessageId);

        // 6..(6 + contentBytes.Length - 1) => the ASCII content
        contentBytes.CopyTo(buffer.AsSpan(6));

        // The last byte is a zero terminator
        buffer[^1] = 0;
        return buffer;
    }

    /// <summary>
    /// Parse a REPLY message from the given raw bytes.
    /// Throws ArgumentException if invalid.
    /// </summary>
    public static Reply FromBytes(ReadOnlySpan<byte> data)
    {
        // Must have at least: 1 byte (type) + 2 bytes (msgId) + 1 byte (result)
        //                     + 2 bytes (refId) + 1 byte (terminator) = 7 minimum
        if (data.Length < 7)
            throw new ArgumentException("Invalid REPLY (too short).");

        if (data[0] != (byte)MessageType.REPLY)
            throw new ArgumentException("Message is not a REPLY type.");

        // 1..2) The REPLY's own message ID
        ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));

        // 3) The result (0 or 1)
        bool result = (data[3] == 1);

        // 4..5) The reference (which message ID are we replying to?)
        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));

        // 6..) The content (up to 0 terminator)
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

    /// <summary>
    /// (Optional) If your protocol also supports a TCP-friendly string form
    /// like "REPLY OK IS somethingHere", you can parse it. 
    /// </summary>
    public static Reply FromTcpString(string[] words)
    {
        if (words.Length != 4 || words[0] != "REPLY")
            throw new ArgumentException("Invalid REPLY format");

        bool result = words[1].ToUpperInvariant() switch
        {
            "OK" => true,
            "NOK" => false,
            _ => throw new ArgumentException("Invalid REPLY result")
        };

        if (words[2] != "IS")
            throw new ArgumentException("Invalid REPLY keyword");

        string content = words[3];
        if (content.Length > 1400 
            || !Regex.IsMatch(content, @"^[\x20-\x7E\s]*$"))
            throw new ArgumentException("Invalid REPLY content");

        return new Reply
        {
            // For TCP-based replies, we might not have a numeric ID context:
            MessageId = 0,
            Result = result,
            RefMessageId = 0,
            MessageContent = content
        };
    }
}
