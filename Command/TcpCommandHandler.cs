using System.Text;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using Message;

public static class TcpCommandHandler
{
    public static async Task<bool> HandleAuth(string[] words, NetworkStream stream, Action<string> setDisplayName)
    {
        if (words.Length != 4)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /auth <username> <secret> <displayName>");
            return false;
        }

        try
        {
            var auth = new Auth
            {
                Username = words[1],
                Secret = words[2],
                DisplayName = words[3]
            };
            setDisplayName(words[3]);
            var data = Encoding.ASCII.GetBytes(auth.ToTcpString());
            await stream.WriteAsync(data);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERR: {e.Message}");
            return false;
        }
    }

    public static async Task<bool> HandleJoin(string[] words, string? displayName, NetworkStream stream)
    {
        if (displayName == null)
        {
            Console.Error.WriteLine("ERR: You must authenticate first.");
            return false;
        }

        if (words.Length != 2)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /join <channelId>");
            return false;
        }

        try
        {
            var join = new Join
            {
                ChannelId = words[1],
                DisplayName = displayName
            };
            var data = Encoding.ASCII.GetBytes(Join.ToTcpString(join));
            await stream.WriteAsync(data);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERR: {e.Message}");
            return false;
        }
    }

    public static bool HandleRename(string[] words, ref string? displayName)
    {
        if (words.Length != 2)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /rename <displayName>");
            return false;
        }

        var name = words[1];
        if (name.Length > 20 || !Regex.IsMatch(name, @"^[\x20-\x7E]*$"))
        {
            Console.Error.WriteLine("ERR: Invalid display name format");
            return false;
        }

        displayName = name;
        Console.WriteLine($"Renamed to {displayName}");
        return true;
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
            Console.Error.WriteLine("ERR: You must authenticate before sending messages");
            return;
        }

        try
        {
            var msg = new Msg { DisplayName = displayName, MessageContents = input };
            var data = Encoding.ASCII.GetBytes(Msg.ToTcpString(msg));
            await stream.WriteAsync(data);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"ERR: {e.Message}");
        }
    }
}
