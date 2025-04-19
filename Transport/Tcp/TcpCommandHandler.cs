using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public class TcpCommandHandler
{
    private readonly TcpStateManager _stateManager;

    public TcpCommandHandler(TcpStateManager stateManager)
    {
        _stateManager = stateManager;
    }

    public async Task HandleUserInput(Socket socket, Arguments args)
    {
        while (true)
        {
            var input = Console.ReadLine();
            if (input == null)
            {
                await _stateManager.DisconnectAsync(socket);
                return;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var currentState = _stateManager.GetState();

            if (tokens[0].StartsWith("/help")){
                Udp.HandleHelp();
                continue;
            }
            else if (currentState == State.start && !tokens[0].StartsWith("/auth"))
            {
                Console.WriteLine("ERROR: You must be authenticated before sending messages");
                continue;
            }

            using var stream = new NetworkStream(socket, ownsSocket: false);

            switch (tokens[0])
            {
                case "/help":
                    Udp.HandleHelp();
                    break;

                case "/auth" when currentState is State.start or State.auth:
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
                    _stateManager.DisplayName = authMsg.DisplayName;
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(authMsg.ToTcpString()));
                    _stateManager.SetState(State.auth);
                    break;

                case "/auth" when currentState == State.open:
                    Console.WriteLine("ERROR: Already authenticated – cannot use /auth again.");
                    break;

                case "/join" when currentState == State.open:
                    if (tokens.Length != 2 || _stateManager.DisplayName == null)
                    {
                        Console.Error.WriteLine("ERR: Usage: /join <channelId>");
                        break;
                    }
                    var joinMsg = new TcpMessage
                    {
                        Type = MessageType.JOIN,
                        ChannelId = tokens[1],
                        DisplayName = _stateManager.DisplayName
                    };
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(joinMsg.ToTcpString()));
                    break;

                case "/rename" when currentState == State.open:
                    if (tokens.Length != 2)
                    {
                        Console.Error.WriteLine("ERR: Usage: /rename <displayName>");
                        break;
                    }
                    _stateManager.DisplayName = tokens[1];
                    Console.WriteLine($"Renamed to {_stateManager.DisplayName}");
                    break;

                default:
                    if (tokens[0].StartsWith("/"))
                    {
                        Console.Error.WriteLine("ERR: Unknown or disallowed command");
                    }
                    else if (currentState == State.open)
                    {
                        var msg = new TcpMessage
                        {
                            Type = MessageType.MSG,
                            DisplayName = _stateManager.DisplayName ?? "?",
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
}
}
