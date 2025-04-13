using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Buffers.Binary;
using Message;
using Command;

public class Udp
{
    private static ushort _messageId = 0;
    private static HashSet<ushort> _receivedIds = new();
    private const int RetryCount = 3;
    private const int ConfirmTimeout = 250;
    private static readonly Mutex _stateLock = new();
    private static State _state = State.start;
    private static string? _userDisplayName;

    public static async Task RunClientSession(Arguments args, IPAddress serverIp)
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var serverEP = new IPEndPoint(serverIp, args.Port);
        IPEndPoint? dynamicServerEP = null;

        // AUTHENTICATION
        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null || !line.StartsWith("/auth ")) continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4) continue;

            var auth = new Auth
            {
                Username = tokens[1],
                Secret = tokens[2],
                DisplayName = tokens[3]
            };

            _userDisplayName = auth.DisplayName;
            byte[] authBytes = auth.ToBytes(_messageId);
            if (!await SendWithConfirm(client, authBytes, serverEP, _messageId)) return;

            UdpReceiveResult replyRes;
            while (true)
            {
                replyRes = await client.ReceiveAsync();
                var buffer = replyRes.Buffer;

                ushort id = ReadMessageId(buffer);
                if (_receivedIds.Contains(id))
                {
                    await SendConfirmIfNeeded(client, buffer, replyRes.RemoteEndPoint);
                    continue;
                }

                if (buffer[0] == (byte)MessageType.REPLY) break;

                await SendConfirmIfNeeded(client, buffer, replyRes.RemoteEndPoint);
            }

            var reply = Reply.FromBytes(replyRes.Buffer);
            _receivedIds.Add(reply.MessageId);
            dynamicServerEP = replyRes.RemoteEndPoint;

            var replyConfirm = new Confirm { RefMessageId = reply.MessageId };
            byte[] replyConfirmBytes = replyConfirm.ToBytes(0);
            await client.SendAsync(replyConfirmBytes, replyConfirmBytes.Length, dynamicServerEP);

            if (!reply.Result) return;
            SetState(State.auth);
            _messageId++;

            // Wait for MSG after AUTH
            while (true)
            {
                var msgRes = await client.ReceiveAsync();
                var buffer = msgRes.Buffer;
                var msgType = (MessageType)buffer[0];

                ushort id = ReadMessageId(buffer);
                if (_receivedIds.Contains(id))
                {
                    await SendConfirmIfNeeded(client, buffer, msgRes.RemoteEndPoint);
                    continue;
                }

                if (msgType == MessageType.MSG)
                {
                    var msg = Msg.FromBytes(buffer);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                    await SendConfirmIfNeeded(client, buffer, msgRes.RemoteEndPoint);
                    SetState(State.open);
                    break;
                }

                await SendConfirmIfNeeded(client, buffer, msgRes.RemoteEndPoint);
            }
            break;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                var incoming = await client.ReceiveAsync();
                var buffer = incoming.Buffer;
                var from = incoming.RemoteEndPoint;
                ushort id = ReadMessageId(buffer);

                if (_receivedIds.Contains(id))
                {
                    await SendConfirmIfNeeded(client, buffer, from);
                    continue;
                }

                MessageType type = (MessageType)buffer[0];

                if (type == MessageType.MSG)
                {
                    var msg = Msg.FromBytes(buffer);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                }

                await SendConfirmIfNeeded(client, buffer, from);
            }
        });

        // SENDING MESSAGES
        while (true)
        {
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (_state != State.open) continue;

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
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    var join = new Join
                    {
                        ChannelId = tokens[1],
                        DisplayName = _userDisplayName ?? "?"
                    };

                    ushort joinId;
                    lock (_stateLock)
                    {
                        joinId = _messageId++;
                    }

                    byte[] joinBytes = join.ToBytes(joinId);
                    if (!await SendWithConfirm(client, joinBytes, dynamicServerEP!, joinId)) continue;
                }
                continue;
            }

            var message = new Msg
            {
                DisplayName = _userDisplayName ?? "?",
                MessageContents = input
            };

            ushort currentId;
            lock (_stateLock)
            {
                currentId = _messageId;
                _messageId++;
            }

            byte[] msgBytes = message.ToBytes(currentId);
            _ = await SendWithConfirm(client, msgBytes, dynamicServerEP!, currentId);
        }
    }

    private static void SetState(State newState)
    {
        _stateLock.WaitOne();
        _state = newState;
        _stateLock.ReleaseMutex();
    }

    private static async Task<bool> SendWithConfirm(UdpClient client, byte[] message, IPEndPoint target, ushort msgId)
    {
        for (int attempt = 1; attempt <= RetryCount; attempt++)
        {
            await client.SendAsync(message, message.Length, target);
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < ConfirmTimeout)
            {
                if (client.Available == 0)
                {
                    await Task.Delay(10);
                    continue;
                }

                var result = await client.ReceiveAsync();
                var data = result.Buffer;
                var from = result.RemoteEndPoint;

                await SendConfirmIfNeeded(client, data, from);

                if (data.Length >= 3 && data[0] == (byte)MessageType.CONFIRM)
                {
                    var confirm = Confirm.FromBytes(data);
                    if (confirm.RefMessageId == msgId) return true;
                }
            }
        }
        return false;
    }

    private static async Task SendConfirmIfNeeded(UdpClient client, byte[] buffer, IPEndPoint from)
    {
        MessageType type = (MessageType)buffer[0];
        ushort msgId = ReadMessageId(buffer);

        if (_receivedIds.Contains(msgId)) return;

        if (type is MessageType.REPLY or MessageType.MSG or MessageType.PING)
        {
            _receivedIds.Add(msgId);
            var confirm = new Confirm { RefMessageId = msgId };
            byte[] confirmBytes = confirm.ToBytes(0);
            await client.SendAsync(confirmBytes, confirmBytes.Length, from);
        }
    }

    private static ushort ReadMessageId(byte[] data)
    {
        if (data.Length < 3) return 0;
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
    }
}



















