using System;
using System.Net;
using System.Net.Sockets;
using Message;
using System.Threading;

public class Udp
{
    private static Mutex _mutexSate = new();
    private static State _state = State.start;

    private static Queue<Confirm> _confirmList = new();
    private static Queue<Reply> _replyList = new();

    private static Mutex _mutexConfirm = new();
    private static Mutex _mutexReply = new();

    private static string? _displayName;
    public static ushort id = 0;

    public enum State
    {
        start,
        auth,
        open,
        error,
        end
    }

    public static async Task RunClientSession(Arguments options, IPAddress ip)
    {
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var serverEP = new IPEndPoint(ip, options.Port);

        var commandHandler = new UdpCommandHandler(
            udpClient,
            serverEP,
            options,
            () => WaitReply(options),
            (bytes) => SendMessageAsync(bytes, udpClient, serverEP, options),
            () => SendBye(udpClient, serverEP, options)
        );

        Console.CancelKeyPress += async (sender, e) =>
        {
            await SendBye(udpClient, serverEP, options);
            udpClient.Close();
            Environment.Exit(0);
        };

        var receiver = Task.Run(() => ReceiveMessage(udpClient, serverEP, options));
        var reader = Task.Run(() => ReadInput(commandHandler));

        await Task.WhenAny(receiver, reader);
    }

    private static async Task<IPEndPoint> ReceiveMessage(UdpClient client, IPEndPoint serverEP, Arguments options)
    {
        try
        {
            while (true)
            {
                var result = await client.ReceiveAsync();

                _mutexSate.WaitOne();
                var currentState = _state;
                _mutexSate.ReleaseMutex();

                if (currentState == State.start)
                    continue;

                if ((result.RemoteEndPoint.Port != serverEP.Port) && currentState == State.auth)
                    serverEP = result.RemoteEndPoint;

                switch ((MessageType)result.Buffer[0])
                {
                    case MessageType.CONFIRM:
                        _mutexConfirm.WaitOne();
                        _confirmList.Enqueue(Confirm.FromBytes(result.Buffer));
                        _mutexConfirm.ReleaseMutex();
                        break;

                    case MessageType.REPLY:
                        _ = SendConfirm(result.Buffer, client, serverEP);
                        var reply = Reply.FromBytes(result.Buffer);
                        Console.Error.WriteLine(reply.Result ? $"Success: {reply.MessageContent}" : $"Failure: {reply.MessageContent}");

                        _mutexReply.WaitOne();
                        _replyList.Enqueue(reply);
                        _mutexReply.ReleaseMutex();
                        break;

                    case MessageType.ERR:
                        _ = SendConfirm(result.Buffer, client, serverEP);
                        var err = Err.FromBytes(result.Buffer);
                        Console.Error.WriteLine($"ERR FROM {err.DisplayName}: {err.MessageContents}");
                        await SendBye(client, serverEP, options);
                        Environment.Exit(0);
                        return serverEP;;

                    case MessageType.MSG:
                        _ = SendConfirm(result.Buffer, client, serverEP);
                        var msg = Msg.FromBytes(result.Buffer);
                        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                        break;

                    case MessageType.BYE:
                        _ = SendConfirm(result.Buffer, client, serverEP);
                        Environment.Exit(0);
                        return serverEP;;

                    default:
                        Console.WriteLine("ERR: wrong data from server");
                        await SendBye(client, serverEP, options);
                        Environment.Exit(0);
                        return serverEP;;
                }
            }
        }
        catch (ArgumentException)
        {
            Console.WriteLine("ERR: ArgumentException");
            throw;
        }
    }

    private static async Task ReadInput(UdpCommandHandler handler)
    {
        while (true)
        {
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            _mutexSate.WaitOne();
            var state = _state;
            _mutexSate.ReleaseMutex();

            if (words[0] == "/help")
            {
                TcpCommandHandler.HandleHelp(); // reused for printing
                continue;
            }

            switch (state)
            {
                case State.start:
                case State.auth:
                    if (words[0] == "/auth")
                    {
                        _mutexSate.WaitOne();
                        _state = State.auth;
                        _mutexSate.ReleaseMutex();

                        bool success = await handler.HandleAuth(words, name => _displayName = name);
                        if (success)
                        {
                            _mutexSate.WaitOne();
                            _state = State.open;
                            _mutexSate.ReleaseMutex();
                        }
                    }
                    else Console.Error.WriteLine("ERR: You must authenticate first.");
                    break;

                case State.open:
                    if (words[0] == "/join")
                        await handler.HandleJoin(words, _displayName);
                    else if (words[0] == "/rename")
                        handler.HandleRename(words, ref _displayName);
                    else if (words[0].StartsWith("/"))
                        Console.Error.WriteLine("ERR: Unknown command.");
                    else
                        await handler.HandleMessage(input, _displayName);
                    break;
            }
        }
    }

    private static async Task<int> SendMessageAsync(byte[] message, UdpClient client, IPEndPoint ep, Arguments options)
    {
        await client.SendAsync(message, message.Length, ep);

        for (int i = 0; i < options.MaxRetries; i++)
        {
            Thread.Sleep(options.UdpTimeout);
            _mutexConfirm.WaitOne();
            while (_confirmList.Count > 0)
            {
                var confirm = _confirmList.Dequeue();
                if (confirm.RefMessageId == BitConverter.ToUInt16(message, 1))
                {
                    _mutexConfirm.ReleaseMutex();
                    id++;
                    return 1;
                }
            }
            _mutexConfirm.ReleaseMutex();
        }

        id++;
        Console.Error.WriteLine("ERR: No confirm");
        return 0;
    }

    private static async Task SendConfirm(byte[] message, UdpClient client, IPEndPoint ep)
    {
        var confirm = new Confirm
        {
            RefMessageId = BitConverter.ToUInt16(message, 1)
        };

        byte[] data = confirm.ToBytes(id);
        await client.SendAsync(data, data.Length, ep);
    }

    private static async Task SendBye(UdpClient client, IPEndPoint ep, Arguments options)
    {
        _mutexSate.WaitOne();
        _state = State.end;
        _mutexSate.ReleaseMutex();
        var bye = new Bye();
        await SendMessageAsync(bye.ToBytes(id), client, ep, options);
    }

    private static async Task<int> WaitReply(Arguments options)
    {
        for (int i = 0; i < options.MaxRetries; i++)
        {
            await Task.Delay(options.UdpTimeout);
            _mutexReply.WaitOne();
            while (_replyList.Count > 0)
            {
                var reply = _replyList.Dequeue();
                if (reply.RefMessageId == (id - 1))
                {
                    _mutexReply.ReleaseMutex();
                    return reply.Result ? 1 : 0;
                }
            }
            _mutexReply.ReleaseMutex();
        }

        Console.Error.WriteLine("ERR: no reply");
        return -1;
    }
}



