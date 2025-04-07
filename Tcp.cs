using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Text;
using Message;

public class Tcp
{
    private enum SessionState { start, auth, open, error, end}

    private static readonly Mutex _stateLock = new();
    private static SessionState _sessionState = SessionState.start;

    private static readonly Mutex _responseLock = new();
    private static int _lastReplyStatus = -1;

    private static string? _userDisplayName;

    public static async Task RunClientSession(Arguments args)
    {
        using var client = new TcpClient();
        try
        {
            client.Connect(args.Ip, args.Port);
            var receiver = ListenForServerMessages(client);
            var sender = HandleUserInput(client);
            await Task.WhenAll(receiver, sender);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERR: {ex.Message}");
        }
    }

    private static async Task HandleUserInput(TcpClient client)
    {
        var stream = client.GetStream();

        Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            await SendDisconnectNotice(client);
            client.Close();
            Environment.Exit(0);
        };

        while (true)
        {
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            _stateLock.WaitOne();
            var currentState = _sessionState;
            _stateLock.ReleaseMutex();

            switch (tokens[0])
            {
                case "/help":
                    TcpCommandHandler.HandleHelp();
                    break;

                case "/auth" when currentState == SessionState.start || currentState == SessionState.auth:
                    bool isAuthenticated = await TcpCommandHandler.HandleAuth(tokens, stream, name => _userDisplayName = name);
                    if (isAuthenticated)
                    {
                        _stateLock.WaitOne();
                        _sessionState = SessionState.auth;
                        _stateLock.ReleaseMutex();
                    }
                    break;

                case "/join" when currentState == SessionState.open:
                    await TcpCommandHandler.HandleJoin(tokens, _userDisplayName, stream);
                    break;

                case "/rename" when currentState == SessionState.open:
                    TcpCommandHandler.HandleRename(tokens, ref _userDisplayName);
                    break;

                default:
                    if (tokens[0].StartsWith("/"))
                    {
                        Console.Error.WriteLine("ERR: Unknown or disallowed command");
                    }
                    else if (currentState == SessionState.open)
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

    private static async Task SendDisconnectNotice(TcpClient client)
    {
        var message = Encoding.UTF8.GetBytes("BYE\r\n");
        await client.GetStream().WriteAsync(message);
    }

    private static async Task ListenForServerMessages(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[2048];

        while (true)
        {
            int byteCount = await stream.ReadAsync(buffer);
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
                    DisplayIncomingMessage(line);
                }
                else if (line.StartsWith("ERR"))
                {
                    HandleServerError(line, client);
                    return;
                }
                else if (line.StartsWith("BYE"))
                {
                    client.Close();
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

            _responseLock.WaitOne();
            _lastReplyStatus = reply.Result ? 1 : 0;
            Console.WriteLine(reply.Result
                ? $"Success: {reply.MessageContent}"
                : $"Failure: {reply.MessageContent}");
            _responseLock.ReleaseMutex();

            if (_lastReplyStatus == 1)
            {
                _stateLock.WaitOne();
                _sessionState = SessionState.open;
                _stateLock.ReleaseMutex();
            }
        }
        catch
        {
            Console.Error.WriteLine("ERR: Failed to parse REPLY");
        }
    }

    private static void DisplayIncomingMessage(string line)
    {
        var parts = line.Split(" IS ", 2);
        var sender = parts[0].Substring("MSG FROM ".Length);
        var message = parts[1];
        Console.WriteLine($"{sender}: {message}");
    }

    private static void HandleServerError(string line, TcpClient client)
    {
        var parts = line.Split(" IS ", 2);
        var sender = parts[0].Substring("ERR FROM ".Length);
        var error = parts[1];
        Console.Error.WriteLine($" ERROR FROM {sender}: {error}");

        _ = SendDisconnectNotice(client);
        client.Close();
        Environment.Exit(0);
    }
}