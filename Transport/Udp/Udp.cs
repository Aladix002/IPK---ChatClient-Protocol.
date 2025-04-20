using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Buffers.Binary;
using Message;

namespace Transport
{
public class Udp : IChatClient
{
    private readonly Arguments _args;
    private readonly IPAddress _serverIp;
    private ushort _messageId = 0;
    private readonly HashSet<ushort> _receivedIds = new();
    private readonly Mutex _stateLock = new();
    private State _state = State.start;
    private string? _userDisplayName;
    private UdpClient? _client;
    private IPEndPoint? _dynamicServerEP;
    private IPEndPoint? _serverEP;

    public Udp(Arguments args, IPAddress serverIp)
    {
        _args = args;
        _serverIp = serverIp;
    }

    public async Task Run()
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        _serverEP = new IPEndPoint(_serverIp, _args.Port);
        await RunClientSession();
    }

    public async Task Stop()
    {
        if (_dynamicServerEP != null && _client != null && _state == State.open)
        {
            ushort byeId = GetNextMessageId();
            var bye = new Bye { DisplayName = _userDisplayName ?? "?" };
            byte[] byeBytes = bye.ToBytes(byeId);
            await _client.SendAsync(byeBytes, byeBytes.Length, _dynamicServerEP);
        }

        SetState(State.end);
        _client?.Close();
    }

    private async Task RunClientSession()
    {
        if (_client == null || _serverEP == null) return;

        await Authenticate();
        if (_state != State.open) return;

        _ = Task.Run(ReceiveLoop);

        while (true)
        {
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input == "/help")
            {
                HandleHelp();
                continue;
            }


            if (input.StartsWith("/rename "))
            {
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    _userDisplayName = tokens[1];
                    Console.WriteLine($"Renamed to '{_userDisplayName}'");
                }
                continue;
            }

            if (input.StartsWith("/join "))
            {
                if (_state != State.open)
                {
                    Console.WriteLine("ERROR: You must authenticate first");
                    continue;
                }
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    var join = new Join { ChannelId = tokens[1], DisplayName = _userDisplayName ?? "?" };
                    ushort joinId = GetNextMessageId();
                    byte[] joinBytes = join.ToBytes(joinId);
                    if (!await SendWithConfirm(joinBytes, _dynamicServerEP!, joinId)) continue;
                }
                continue;
            }

            if (input == "/help")
            {
                HandleHelp();
                continue;
            }

            if (input.StartsWith("/"))
            {
                Console.WriteLine("ERROR: Unknown or disallowed command");
                continue;
            }

            if (_state != State.open)
            {
                Console.WriteLine("ERROR: You must authenticate first");
                continue;
            }

            var msg = new Msg { DisplayName = _userDisplayName ?? "?", MessageContents = input };
            ushort msgId = GetNextMessageId();
            byte[] msgBytes = msg.ToBytes(msgId);
            await SendWithConfirm(msgBytes, _dynamicServerEP!, msgId);
        }
    }

    private async Task Authenticate()
    {
        while (_state == State.start)
        {
            string? line = Console.ReadLine();
            if (line == null || !line.StartsWith("/auth "))
            {
                Console.WriteLine("ERROR: You must authenticate first");
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4)
            {
                Console.WriteLine("ERROR: Invalid AUTH format");
                continue;
            }

            var auth = new Auth { Username = tokens[1], Secret = tokens[2], DisplayName = tokens[3] };
            _userDisplayName = auth.DisplayName;
            ushort authId = GetNextMessageId();
            byte[] authBytes = auth.ToBytes(authId);

            if (!await SendWithConfirm(authBytes, _serverEP!, authId)) return;

            while (true)
            {
                var replyRes = await _client!.ReceiveAsync();
                var buffer = replyRes.Buffer;
                ushort id = ReadMessageId(buffer);
                if (_receivedIds.Contains(id)) continue;

                if ((MessageType)buffer[0] == MessageType.REPLY)
                {
                    var reply = Reply.FromBytes(buffer);
                    _receivedIds.Add(reply.MessageId);
                    _dynamicServerEP = replyRes.RemoteEndPoint;

                    // Always send CONFIRM before handling the result
                    await SendConfirm(reply.MessageId);

                    if (reply.Result)
                    {
                        Console.WriteLine($"Action Success: {reply.MessageContent}");
                        SetState(State.auth);
                    }
                    else
                    {
                        Console.WriteLine($"Action Failure: {reply.MessageContent}");
                        return; // early return, but confirm is already sent
                    }
                    break;
                }
            }

            while (true)
            {
                var msgRes = await _client!.ReceiveAsync();
                var buffer = msgRes.Buffer;
                if ((MessageType)buffer[0] == MessageType.MSG)
                {
                    var msg = Msg.FromBytes(buffer);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                    await SendConfirm(msg.MessageId);
                    SetState(State.open);
                    break;
                }
            }
        }
    }

    private async Task ReceiveLoop()
    {
        while (_state != State.end)
        {
            var incoming = await _client!.ReceiveAsync();
            var buffer = incoming.Buffer;
            var from = incoming.RemoteEndPoint;
            ushort id = ReadMessageId(buffer);

            if (_receivedIds.Contains(id)) continue;

            if ((MessageType)buffer[0] == MessageType.MSG)
            {
                var msg = Msg.FromBytes(buffer);
                Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
            }

            await SendConfirm(id, from);
            _receivedIds.Add(id);
        }
    }

    private ushort GetNextMessageId()
    {
        _stateLock.WaitOne();
        ushort id = _messageId;
        _messageId++;
        _stateLock.ReleaseMutex();
        return id;
    }

    private void SetState(State newState)
    {
        _stateLock.WaitOne();
        _state = newState;
        _stateLock.ReleaseMutex();
    }

    private async Task<bool> SendWithConfirm(byte[] message, IPEndPoint target, ushort msgId)
    {
        for (int attempt = 1; attempt <= _args.MaxRetries; attempt++)
        {
            await _client!.SendAsync(message, message.Length, target);
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < _args.UdpTimeout)
            {
                if (_client.Available > 0)
                {
                    var result = await _client.ReceiveAsync();
                    var data = result.Buffer;
                    if (data[0] == (byte)MessageType.CONFIRM)
                    {
                        var confirm = Confirm.FromBytes(data);
                        if (confirm.RefMessageId == msgId) return true;
                    }
                }
                await Task.Delay(10);
            }
        }
        Console.WriteLine($"ERR: No confirm received for ID={msgId}.");
        return false;
    }

    private async Task SendConfirm(ushort refId, IPEndPoint? target = null)
    {
        var confirm = new Confirm { RefMessageId = refId };
        var confirmBytes = confirm.ToBytes(0);
        await _client!.SendAsync(confirmBytes, confirmBytes.Length, target ?? _dynamicServerEP!);
    }

    private ushort ReadMessageId(byte[] data)
    {
        return data.Length >= 3 ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2)) : (ushort)0;
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
}





