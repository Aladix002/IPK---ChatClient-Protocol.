using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Message;
using Command;

public class Udp
{
    private static Mutex _stateLock = new();
    private static State _state = State.start;

    private static Mutex _confirmLock = new();
    private static Queue<Confirm> _confirmQueue = new();

    private static Mutex _replyLock = new();
    private static Queue<Reply> _replyQueue = new();

    private static string? _displayName;
    public static ushort id = 0;

    public static async Task RunClientSession(Arguments options, IPAddress ip)
    {
        var client = new UdpClient();
        var serverEP = new IPEndPoint(ip, options.Port);
        var clientEP = new IPEndPoint(IPAddress.Any, 0);
        client.Client.Bind(clientEP);

        var receiveTask = ReceiveMessages(client, serverEP);
        var sendTask = HandleUserInput(client, serverEP, options);

        await Task.WhenAny(receiveTask, sendTask);
    }

    private static async Task HandleUserInput(UdpClient client, IPEndPoint serverEP, Arguments options)
    {
        Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            await SendBye(client, serverEP, options);
            Environment.Exit(0);
        };

        var handler = new UdpCommandHandler(
            client,
            serverEP,
            options,
            WaitReply,
            bytes => SendMessageAsync(bytes, client, serverEP, options),
            () => SendBye(client, serverEP, options)
        );

        while (true)
        {
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            _stateLock.WaitOne();
            var currentState = _state;
            _stateLock.ReleaseMutex();

            switch (tokens[0])
            {
                case "/help":
                    TcpCommandHandler.HandleHelp();
                    break;

                case "/auth" when currentState == State.start || currentState == State.auth:
                    {
                        bool success = await handler.HandleAuth(tokens, name =>
                        {
                            _displayName = name;
                            _stateLock.WaitOne();
                            _state = State.auth;
                            _stateLock.ReleaseMutex();
                        });

                        if (success)
                        {
                            _stateLock.WaitOne();
                            _state = State.open;
                            _stateLock.ReleaseMutex();
                        }
                        break;
                    }

                case "/join" when currentState == State.open:
                    await handler.HandleJoin(tokens, _displayName);
                    break;

                case "/rename" when currentState == State.open:
                    handler.HandleRename(tokens, ref _displayName);
                    break;

                default:
                    if (tokens[0].StartsWith("/"))
                        Console.Error.WriteLine("ERR: Unknown or disallowed command");
                    else if (currentState == State.open)
                        await handler.HandleMessage(input, _displayName);
                    else
                        Console.Error.WriteLine("ERR: You must be authenticated first");
                    break;
            }
        }
    }

    private static async Task ReceiveMessages(UdpClient client, IPEndPoint serverEP)
    {
        while (true)
        {
            var result = await client.ReceiveAsync();
            var data = result.Buffer;

            switch ((MessageType)data[0])
            {
                case MessageType.CONFIRM:
                    _confirmLock.WaitOne();
                    _confirmQueue.Enqueue(Confirm.FromBytes(data));
                    _confirmLock.ReleaseMutex();
                    break;

                case MessageType.REPLY:
                    var reply = Reply.FromBytes(data);
                    await SendConfirm(reply.RefMessageId, client, serverEP);
                    Console.WriteLine(reply.Result ? $"Success: {reply.MessageContent}" : $"Failure: {reply.MessageContent}");

                    _replyLock.WaitOne();
                    _replyQueue.Enqueue(reply);
                    _replyLock.ReleaseMutex();
                    break;

                case MessageType.MSG:
                    var msg = Msg.FromBytes(data);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                    await SendConfirm(msg.MessageId, client, serverEP);
                    break;

                case MessageType.ERR:
                    var err = Err.FromBytes(data);
                    Console.Error.WriteLine($"ERR FROM {err.DisplayName}: {err.MessageContents}");
                    await SendConfirm(err.MessageId, client, serverEP);
                    await SendBye(client, serverEP, new Arguments());
                    Environment.Exit(0);
                    break;

                case MessageType.BYE:
                    await SendConfirm(BitConverter.ToUInt16(data, 1), client, serverEP);
                    client.Close();
                    Environment.Exit(0);
                    break;
            }
        }
    }

    private static async Task<int> SendMessageAsync(byte[] data, UdpClient client, IPEndPoint ep, Arguments opt)
    {
        await client.SendAsync(data, data.Length, ep);

        for (int i = 0; i < opt.MaxRetries; i++)
        {
            Thread.Sleep(opt.UdpTimeout);

            _confirmLock.WaitOne();
            while (_confirmQueue.Count > 0)
            {
                var confirm = _confirmQueue.Dequeue();
                if (confirm.RefMessageId == BitConverter.ToUInt16(data, 1))
                {
                    _confirmLock.ReleaseMutex();
                    id++;
                    return 1;
                }
            }
            _confirmLock.ReleaseMutex();
        }

        id++;
        Console.Error.WriteLine("ERR: No confirm");
        return 0;
    }

    private static async Task<int> WaitReply()
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(100);

            _replyLock.WaitOne();
            if (_replyQueue.Count > 0)
            {
                var reply = _replyQueue.Dequeue();
                _replyLock.ReleaseMutex();
                return reply.Result ? 1 : 0;
            }
            _replyLock.ReleaseMutex();
        }

        Console.Error.WriteLine("ERR: No reply");
        return -1;
    }

    private static async Task SendConfirm(ushort refId, UdpClient client, IPEndPoint ep)
    {
        var confirm = new Confirm { RefMessageId = refId };
        var bytes = confirm.ToBytes(id);
        await client.SendAsync(bytes, bytes.Length, ep);
    }

    private static async Task SendBye(UdpClient client, IPEndPoint ep, Arguments opt)
    {
        _stateLock.WaitOne();
        _state = State.end;
        _stateLock.ReleaseMutex();

        var bye = new Bye();
        await SendMessageAsync(bye.ToBytes(id), client, ep, opt);
    }
}




