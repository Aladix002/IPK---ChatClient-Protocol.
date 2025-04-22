using System;
using System.Net.Sockets;
using System.Text;
using Message;

namespace Transport;

public class TcpCommandHandler
{
    private readonly Tcp _tcp;

    public TcpCommandHandler(Tcp tcp)
    {
        _tcp = tcp;
    }

    public async Task HandleUserInput()
    {
        while (true)
        {
            var input = Console.ReadLine();
            if (input == null)
            {
                await _tcp.Stop();
                return;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;
            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            using var stream = new NetworkStream(_tcp.Socket, ownsSocket: false);

            switch (tokens[0])
            {
                case "/help":
                    HandleHelp();
                    break;

                case "/auth" when _tcp.CurrentState is State.start or State.auth:
                    if (tokens.Length != 4)
                    {
                        Console.Error.WriteLine("ERR: Usage: /auth <username> <secret> <displayName>");
                        break;
                    }
                    var authMsg = new TcpMessage
                    {
                        Type = MessageType.AUTH,
                        Username = tokens[1],
                        Secret = tokens[2],
                        DisplayName = tokens[3]
                    };
                    _tcp.SetDisplayName(authMsg.DisplayName);
                    // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.writeasync?view=net-9.0
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(authMsg.ToTcpString()));
                    _tcp.SetState(State.auth);
                    break;

                case "/auth" when _tcp.CurrentState == State.open:
                    Console.WriteLine("ERROR: Already authenticated – cannot use /auth again.");
                    break;

                case "/join" when _tcp.CurrentState == State.open:
                    if (tokens.Length != 2 || _tcp.DisplayName == null)
                    {
                        Console.Error.WriteLine("ERR: Usage: /join <channelId>");
                        break;
                    }
                    var joinMsg = new TcpMessage
                    {
                        Type = MessageType.JOIN,
                        ChannelId = tokens[1],
                        DisplayName = _tcp.DisplayName
                    };
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(joinMsg.ToTcpString()));
                    break;

                case "/rename" when _tcp.CurrentState == State.open:
                    if (tokens.Length != 2)
                    {
                        Console.Error.WriteLine("ERR: Usage: /rename <displayName>");
                        break;
                    }
                    _tcp.SetDisplayName(tokens[1]);
                    Console.WriteLine($"Renamed to {_tcp.DisplayName}");
                    break;

                default:
                    if (tokens[0].StartsWith("/"))
                    {
                        Console.WriteLine("ERROR: Unknown or disallowed command");
                    }
                    else if (_tcp.CurrentState == State.open)
                    {
                        var msg = new TcpMessage
                        {
                            Type = MessageType.MSG,
                            DisplayName = _tcp.DisplayName ?? "?",
                            MessageContents = input
                        };
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(msg.ToTcpString()));
                    }
                    else
                    {
                        Console.WriteLine("ERROR: You must be authenticated before sending messages");
                    }
                    break;
            }
        }
    }

    public static void HandleHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("/auth <username> <secret> <displayName>");
        Console.WriteLine("/join <channelId>");
        Console.WriteLine("/rename <displayName>");
        Console.WriteLine("/help");
    }
}
