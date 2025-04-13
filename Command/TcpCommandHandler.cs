using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Message;

namespace Command;

public static class TcpCommandHandler
{
    public static async Task<bool> HandleAuth(string[] words, NetworkStream stream, Action<string> setDisplayName)
    {
        if (words.Length != 4)
        {
            Console.Error.WriteLine("ERR: Usage: /auth <username> <secret> <displayName>");
            return false;
        }

        var auth = new Auth
        {
            Username = words[1],
            Secret = words[2],
            DisplayName = words[3]
        };

        setDisplayName(auth.DisplayName);

        // Convert the AUTH message to TCP string and send it
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.writeasync?view=net-8.0
        byte[] data = Encoding.ASCII.GetBytes(auth.ToTcpString());
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

        var join = new Join { ChannelId = words[1], DisplayName = displayName };

        // Format and send the JOIN message
        byte[] data = Encoding.ASCII.GetBytes(Join.ToTcpString(join));
        await stream.WriteAsync(data);
    }

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

        if (input.Length > 1400)
        {
            Console.Error.WriteLine("ERR: Message too long");
            return;
        }

        var msg = new Msg { DisplayName = displayName, MessageContents = input };

        // Format and send message to server
        byte[] data = Encoding.ASCII.GetBytes(Msg.ToTcpString(msg));
        await stream.WriteAsync(data);
    }
}


