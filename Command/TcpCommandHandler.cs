using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Message;

namespace Command;

public static class TcpCommandHandler
{
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.writeasync
    public static async Task<bool> HandleAuth(string[] words, NetworkStream stream, Action<string> setDisplayName)
    {
        if (words.Length != 4)
        {
            Console.Error.WriteLine("ERR: Usage: /auth <username> <secret> <displayName>");
            return false;
        }

        var msg = new TcpMessage
        {
            Type = MessageType.AUTH,
            Username = words[1],
            Secret = words[2],
            DisplayName = words[3]
        };

        setDisplayName(msg.DisplayName!);

        byte[] data = Encoding.ASCII.GetBytes(msg.ToTcpString());
        await stream.WriteAsync(data);
        return true;
    }

    public static async Task HandleJoin(string[] words, string? displayName, NetworkStream stream)
    {
        if (displayName == null)
        {
            Console.Error.WriteLine("ERR: Authenticate first");
            return;
        }

        if (words.Length != 2)
        {
            Console.Error.WriteLine("ERR: Usage: /join <channelId>");
            return;
        }

        var msg = new TcpMessage
        {
            Type = MessageType.JOIN,
            DisplayName = displayName,
            ChannelId = words[1]
        };

        byte[] data = Encoding.ASCII.GetBytes(msg.ToTcpString());
        await stream.WriteAsync(data);
    }

    // https://regex101.com/ 
    public static void HandleRename(string[] words, ref string? displayName)
    {
        if (words.Length != 2 || words[1].Length > 20 || !Regex.IsMatch(words[1], @"^[\x20-\x7E]*$"))
        {
            Console.Error.WriteLine("ERR: Invalid display name");
            return;
        }

        displayName = words[1];
        Console.WriteLine($"Renamed to {displayName}");
    }

    public static void HandleHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("/auth <username> <secret> <displayName>");
        Console.WriteLine("/join <channelId>");
        Console.WriteLine("/rename <displayName>");
        Console.WriteLine("/help");
    }

    public static async Task HandleMessage(string input, string? displayName, NetworkStream stream)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            Console.Error.WriteLine("ERR: Authenticate first");
            return;
        }

        if (input.Length > 1400) // limit for message length
        {
            Console.Error.WriteLine("ERR: Message too long");
            return;
        }

        var msg = new TcpMessage
        {
            Type = MessageType.MSG,
            DisplayName = displayName,
            MessageContents = input
        };

        byte[] data = Encoding.ASCII.GetBytes(msg.ToTcpString());
        await stream.WriteAsync(data);
    }
}




