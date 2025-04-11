using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Message;
using Command;

public class Tcp
{
    private static readonly Mutex _stateLock = new();
    private static State _State = State.start;

    private static string? _userDisplayName;

    public static async Task RunClientSession(Arguments args)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            socket.Connect(args.Ip, args.Port);

            var receiver = ListenForServerMessages(socket);
            var sender = HandleUserInput(socket);
            await Task.WhenAll(receiver, sender);
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
            await SendDisconnectNotice(socket);
            socket.Close();
            Environment.Exit(0);
        };

        while (true)
        {
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            _stateLock.WaitOne();
            var currentState = _State;
            _stateLock.ReleaseMutex();

            using var stream = new NetworkStream(socket);

            switch (tokens[0])
            {
                case "/help":
                    TcpCommandHandler.HandleHelp();
                    break;

                case "/auth" when currentState == State.start || currentState == State.auth:
                    {
                        bool ok = await TcpCommandHandler.HandleAuth(tokens, stream, name => _userDisplayName = name);
                        if (ok)
                        {
                            _stateLock.WaitOne();
                            _State = State.auth;
                            _stateLock.ReleaseMutex();
                        }
                        break;
                    }

                case "/join" when currentState == State.open:
                    await TcpCommandHandler.HandleJoin(tokens, _userDisplayName, stream);
                    break;

                case "/rename" when currentState == State.open:
                    TcpCommandHandler.HandleRename(tokens, ref _userDisplayName);
                    break;

                default:
                    if (tokens[0].StartsWith("/"))
                    {
                        Console.Error.WriteLine("ERR: Unknown or disallowed command");
                    }
                    else if (currentState == State.open)
                    {
                        await TcpCommandHandler.HandleMessage(input, _userDisplayName, stream);
                    }
                    else
                    {
                        Console.Error.WriteLine("ERR: You must be authenticated before sending messages");
                    }
                    break;
            }
        }
    }

    private static async Task SendDisconnectNotice(Socket socket)
    {
        byte[] message = Encoding.UTF8.GetBytes("BYE\r\n");
        await socket.SendAsync(message, SocketFlags.None);
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
                if (line.StartsWith("REPLY"))
                {
                    HandleReply(line);
                }
                else if (line.StartsWith("MSG"))
                {
                    DisplayMessage(line);
                }
                else if (line.StartsWith("ERR"))
                {
                    HandleServerError(line, socket);
                    return;
                }
                else if (line.StartsWith("BYE"))
                {
                    socket.Close();
                    Environment.Exit(0);
                }
            }
        }
    }

    private static void HandleReply(string line)
    {
        try
        {
            var parts = line.Split(' ', 4);
            var reply = Reply.FromTcpString(parts);

            Console.WriteLine(reply.Result
                ? $"Success: {reply.MessageContent}"
                : $"Failure: {reply.MessageContent}");

            if (reply.Result)
            {
                _stateLock.WaitOne();
                _State = State.open;
                _stateLock.ReleaseMutex();
            }
        }
        catch
        {
            Console.Error.WriteLine("ERR: Failed to parse REPLY");
        }
    }

    private static void DisplayMessage(string line)
    {
        var parts = line.Split(" IS ", 2);
        var sender = parts[0].Substring("MSG FROM ".Length);
        var message = parts[1];
        Console.WriteLine($"{sender}: {message}");
    }

    private static void HandleServerError(string line, Socket socket)
    {
        var parts = line.Split(" IS ", 2);
        var sender = parts[0].Substring("ERR FROM ".Length);
        var error = parts[1];
        Console.Error.WriteLine($"ERROR FROM {sender}: {error}");

        _ = SendDisconnectNotice(socket);
        socket.Close();
        Environment.Exit(0);
    }
}

