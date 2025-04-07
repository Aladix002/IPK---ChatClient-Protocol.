using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Message;

public class Auth : ITcpMessage
{
    public MessageType MessageType => MessageType.AUTH;

    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Secret { get; init; }

    private static readonly Regex UsernameRegex = new("^[a-zA-Z0-9_-]{1,20}$");
    private static readonly Regex DisplayNameRegex = new("^[\x21-\x7E]{1,20}$");
    private static readonly Regex SecretRegex = new("^[a-zA-Z0-9_-]{1,128}$");

    private void Validate()
    {
        if (!UsernameRegex.IsMatch(Username))
            throw new ArgumentException("Invalid Username");
        if (!DisplayNameRegex.IsMatch(DisplayName))
            throw new ArgumentException("Invalid DisplayName");
        if (!SecretRegex.IsMatch(Secret))
            throw new ArgumentException("Invalid Secret");
    }

    public string ToTcpString()
    {
        Validate();
        return $"AUTH {Username} AS {DisplayName} USING {Secret}\r\n";
    }

    public byte[] ToBytes(ushort messageId)
    {
        Validate();

        byte[] usernameBytes = Encoding.ASCII.GetBytes(Username);
        byte[] displayNameBytes = Encoding.ASCII.GetBytes(DisplayName);
        byte[] secretBytes = Encoding.ASCII.GetBytes(Secret);

        byte[] idBytes = BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((short)messageId));

        byte[] result = new byte[1 + 2 + usernameBytes.Length + 1 + displayNameBytes.Length + 1 + secretBytes.Length + 1];
        result[0] = (byte)MessageType.AUTH;
        result[1] = idBytes[0];
        result[2] = idBytes[1];

        int offset = 3;
        Array.Copy(usernameBytes, 0, result, offset, usernameBytes.Length);
        offset += usernameBytes.Length;
        result[offset++] = 0;

        Array.Copy(displayNameBytes, 0, result, offset, displayNameBytes.Length);
        offset += displayNameBytes.Length;
        result[offset++] = 0;

        Array.Copy(secretBytes, 0, result, offset, secretBytes.Length);
        offset += secretBytes.Length;
        result[offset] = 0;

        return result;
    }
}