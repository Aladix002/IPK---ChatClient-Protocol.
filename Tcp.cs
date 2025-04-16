using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Message;
using Command;

public class Tcp
{
    private static readonly Mutex _stateLock = new(); // Lock to protect state switching between threads
    private static State _State = State.start; // Initial state of the client
    private static bool _shutdownInitiated = false; // Prevent multiple shutdowns

    private static string? _userDisplayName; // Saved after successful auth

    public static async Task RunClientSession(Arguments args)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            socket.Connect(args.Ip, args.Port);

            // Start parallel tasks for reading from console and listening from server
            var receiver = ListenForServerMessages(socket);
            var sender = HandleUserInput(socket);
            await Task.WhenAll(receiver, sender); // wait both to finish
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERR: {ex.Message}");
        }
    }

    private static async Task HandleUserInput(Socket socket)
    {

        Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            await GracefulShutdown(socket);
        };

        while (true)
        {
            var input = Console.ReadLine();
            if (input == null)
            {
                await GracefulShutdown(socket);
                return;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            _stateLock.WaitOne();
            var currentState = _State;
            _stateLock.ReleaseMutex();

            if (currentState == State.start && !tokens[0].StartsWith("/auth"))
            {
                Console.WriteLine("ERROR: You must be authenticated before sending messages");
                continue;
            }

            using var stream = new NetworkStream(socket);

            switch (tokens[0])
            {
                case "/help":
                    TcpCommandHandler.HandleHelp();
                    break;

                // AUTH allowed in start/auth state
                case "/auth" when currentState is State.start or State.auth:
                    {
                        if (tokens.Length != 4)
                        {
                            Console.Error.WriteLine("ERR: Usage: /auth <username> <secret> <displayName>");
                            break;
                        }

                        var msg = new TcpMessage
                        {
                            Type = MessageType.AUTH,
                            DisplayName = tokens[3],
                            Username = tokens[1],
                            Secret = tokens[2]
                        };

                        _userDisplayName = msg.DisplayName;
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(msg.ToTcpString()));

                        _stateLock.WaitOne();
                        _State = State.auth;
                        _stateLock.ReleaseMutex();
                        break;
                    }

                case "/auth" when currentState == State.open:
                    Console.WriteLine("ERROR: Already authenticated – cannot use /auth again.");
                    break;
                
                case "/join" when currentState == State.open:
                    {
                        if (tokens.Length != 2 || _userDisplayName == null)
                        {
                            Console.Error.WriteLine("ERR: Usage: /join <channelId>");
                            break;
                        }

                        var msg = new TcpMessage
                        {
                            Type = MessageType.JOIN,
                            DisplayName = _userDisplayName,
                            ChannelId = tokens[1]
                        };

                        await stream.WriteAsync(Encoding.ASCII.GetBytes(msg.ToTcpString()));
                        break;
                    }

                case "/rename" when currentState == State.open:
                    {
                        if (tokens.Length != 2)
                        {
                            Console.Error.WriteLine("ERR: Usage: /rename <displayName>");
                            break;
                        }

                        _userDisplayName = tokens[1];
                        Console.WriteLine($"Renamed to {_userDisplayName}");
                        break;
                    }

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
                            DisplayName = _userDisplayName ?? "?",
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

    private static async Task GracefulShutdown(Socket socket)
    {
        if (_shutdownInitiated) return;
        _shutdownInitiated = true;

        try
        {
            // Send BYE message before exiting
            var bye = new TcpMessage
            {
                Type = MessageType.BYE,
                DisplayName = _userDisplayName
            };
            await socket.SendAsync(Encoding.ASCII.GetBytes(bye.ToTcpString()), SocketFlags.None);
        }
        catch { }

        socket.Close();
        Environment.Exit(0);
    }

    private static async Task ListenForServerMessages(Socket socket)
    {
        var buffer = new byte[2048];

        while (true)
        {
            int byteCount = await socket.ReceiveAsync(buffer, SocketFlags.None);
            if (byteCount == 0) break;

            var responseText = Encoding.UTF8.GetString(buffer, 0, byteCount);
            var lines = responseText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                TcpMessage msg;
                try
                {
                    msg = TcpMessage.ParseTcp(line);

                    // source: https://learn.microsoft.com/en-us/dotnet/api/system.string.split
                }
                catch (Exception)
                {
                    Console.WriteLine("ERROR: Malformed message received.");

                    // Send ERR message back to server
                    try
                    {
                        var err = new TcpMessage
                        {
                            Type = MessageType.ERR,
                            DisplayName = _userDisplayName ?? "client",
                            MessageContents = "Malformed message received"
                        };
                        await socket.SendAsync(Encoding.ASCII.GetBytes(err.ToTcpString()), SocketFlags.None);
                    }
                    catch { }

                    await GracefulShutdown(socket);
                    return;
                }

                switch (msg.Type)
                {
                    case MessageType.REPLY:
                        HandleReply(msg);
                        break;

                    case MessageType.MSG:
                        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                        break;

                    case MessageType.ERR:
                        Console.WriteLine($"ERROR FROM {msg.DisplayName}: {msg.MessageContents}");
                        await GracefulShutdown(socket);
                        return;

                    case MessageType.BYE:
                        await GracefulShutdown(socket);
                        return;
                }
            }
        }
    }

    private static void HandleReply(TcpMessage reply)
    {
        // Reply message changes client state to open
        Console.WriteLine(reply.Result
            ? $"Action Success: {reply.MessageContents}"
            : $"Action Failure: {reply.MessageContents}");

        if (reply.Result)
        {
            _stateLock.WaitOne();
            _State = State.open;
            _stateLock.ReleaseMutex();
        }
    }
}




