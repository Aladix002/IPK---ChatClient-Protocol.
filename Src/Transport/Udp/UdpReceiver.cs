using System.Net;
using Message;

namespace Transport;

public class UdpReceiver
{
    private readonly Udp _udp;

    public UdpReceiver(Udp udp)
    {
        _udp = udp;
    }

    public async Task ListenForServerMessagesAsync()
    {
        try
        {
            while (!_udp.CancellationToken.IsCancellationRequested)
            {
                var result = await _udp.Client.ReceiveAsync(_udp.CancellationToken);
                var data = result.Buffer;
                var sender = result.RemoteEndPoint;

                if (data.Length < 3)
                    continue;

                UdpMessage msg;
                try
                {
                    msg = UdpMessage.ParseUdp(data);
                }
                catch
                {
                    var confirm = new byte[] { 0x00, data[1], data[2] };
                    _udp.Client.Send(confirm, 3, sender);

                    Console.WriteLine("ERROR: Invalid message format");

                    var err = new UdpMessage
                    {
                        Type = MessageType.ERR,
                        DisplayName = _udp.DisplayName,
                        MessageContents = "Invalid message format"
                    };
                    var errBytes = err.ToBytes(_udp.GetNextMessageId());
                    _udp.SendReliable(errBytes);
                    return;
                }

                if (msg.Type == MessageType.CONFIRM)
                {
                    _udp.Awaiting.TryRemove(msg.MessageId, out _);
                    continue;
                }
                 // potvrdenie spravy
                var confirmMsg = new byte[] { 0x00, data[1], data[2] };
                _udp.Client.Send(confirmMsg, 3, sender);

                if (!_udp.SeenIds.Add(msg.MessageId))
                    continue;

                switch (msg.Type)
                {
                    case MessageType.REPLY:
                        HandleReply(msg, sender);
                        break;
                    case MessageType.MSG:
                        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                        break;
                    case MessageType.BYE:
                        Console.WriteLine("Server: BYE");
                        await _udp.Stop();
                        Environment.Exit(0);
                        break;
                    case MessageType.ERR:
                        Console.WriteLine($"ERROR FROM {msg.DisplayName}: {msg.MessageContents}");
                        _ = _udp.Stop();
                        break;
                    case MessageType.AUTH:
                    case MessageType.JOIN:
                    case MessageType.PING:
                        Console.WriteLine("ERROR: Unexpected message from server");
                        var err = new UdpMessage
                        {
                            Type = MessageType.ERR,
                            DisplayName = _udp.DisplayName,
                            MessageContents = "Unexpected message type from server"
                        };
                        var errBytes = err.ToBytes(_udp.GetNextMessageId());
                        _udp.SendReliable(errBytes);
                        break;
                    default:
                        Console.WriteLine($"ERROR: Unknown message type: {msg.Type}");

                        var unknownErr = new UdpMessage
                        {
                            Type = MessageType.ERR,
                            DisplayName = _udp.DisplayName,
                            MessageContents = $"Unknown message type: {msg.Type}"
                        };
                        var unknownErrBytes = unknownErr.ToBytes(_udp.GetNextMessageId());
                        _udp.SendReliable(unknownErrBytes);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void HandleReply(UdpMessage msg, IPEndPoint sender)
    {
        Console.WriteLine(msg.Result == true
            ? $"Action Success: {msg.MessageContents}"
            : $"Action Failure: {msg.MessageContents}");

        if (_udp.CurrentState == State.auth)
        {
            if (msg.Result == true)
            {
                _udp.SetDynamicEndpoint(sender);
                _udp.SetState(State.open);
            }
            else
            {
                _udp.SetState(State.start);
            }
        }
    }
}
