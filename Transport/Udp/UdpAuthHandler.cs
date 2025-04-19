using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public class UdpAuthHandler
{
    private readonly UdpClient _client;
    private readonly Arguments _args;

    public UdpAuthHandler(UdpClient client, Arguments args)
    {
        _client = client;
        _args = args;
    }

    public async Task<IPEndPoint?> AuthenticateAsync(IPEndPoint serverEP)
    {
        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null) continue;
            if (line.StartsWith("/help"))
            {
                Udp.HandleHelp();
                continue;
            }

            if (!line.StartsWith("/auth ")) continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4) continue;

            var auth = new Auth
            {
                Username = tokens[1],
                Secret = tokens[2],
                DisplayName = tokens[3]
            };

            UdpState.UserDisplayName = auth.DisplayName;
            ushort authId = UdpState.GetNextMessageId();
            byte[] authBytes = auth.ToBytes(authId);

            if (!await UdpConfirmHelper.SendWithConfirm(_client, authBytes, serverEP, authId, _args))
                return null;

            UdpReceiveResult replyRes;
            while (true)
            {
                replyRes = await _client.ReceiveAsync();
                var buffer = replyRes.Buffer;
                ushort id = UdpConfirmHelper.ReadMessageId(buffer);
                UdpState.UpdateMessageIdIfNeeded(id);

                if (UdpState.IsDuplicate(id))
                {
                    await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, replyRes.RemoteEndPoint);
                    continue;
                }

                if (buffer[0] == (byte)MessageType.REPLY)
                    break;

                await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, replyRes.RemoteEndPoint);
            }

            var reply = Reply.FromBytes(replyRes.Buffer);
            UdpState.MarkReceived(reply.MessageId);

            if (reply.Result)
                Console.WriteLine($"Action Success: {reply.MessageContent}");
            else
                Console.WriteLine($"Action Failure: {reply.MessageContent}");

            var replyConfirm = new Confirm { RefMessageId = reply.MessageId };
            byte[] replyConfirmBytes = replyConfirm.ToBytes(0);
            await _client.SendAsync(replyConfirmBytes, replyConfirmBytes.Length, replyRes.RemoteEndPoint);

            if (!reply.Result) return null;

            UdpState.SetState(State.auth);

            // Wait for welcome MSG
            while (true)
            {
                var msgRes = await _client.ReceiveAsync();
                var buffer = msgRes.Buffer;
                ushort id = UdpConfirmHelper.ReadMessageId(buffer);

                if (UdpState.IsDuplicate(id))
                {
                    await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, msgRes.RemoteEndPoint);
                    continue;
                }

                if ((MessageType)buffer[0] == MessageType.MSG)
                {
                    var msg = Msg.FromBytes(buffer);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                    await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, msgRes.RemoteEndPoint);
                    UdpState.SetState(State.open);
                    break;
                }

                await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, msgRes.RemoteEndPoint);
            }

            return replyRes.RemoteEndPoint;
        }
    }
}
}
