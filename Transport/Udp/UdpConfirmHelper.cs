using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public static class UdpConfirmHelper
{
    public static async Task<bool> SendWithConfirm(UdpClient client, byte[] message, IPEndPoint target, ushort msgId, Arguments args)
    {
        for (int attempt = 1; attempt <= args.MaxRetries; attempt++)
        {
            await client.SendAsync(message, message.Length, target);
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < args.UdpTimeout)
            {
                if (client.Available == 0)
                {
                    await Task.Delay(10);
                    continue;
                }

                var result = await client.ReceiveAsync();
                var data = result.Buffer;
                await SendConfirmIfNeeded(client, data, result.RemoteEndPoint);

                if (data.Length >= 3 && data[0] == (byte)MessageType.CONFIRM)
                {
                    var confirm = Confirm.FromBytes(data);
                    if (confirm.RefMessageId == msgId) return true;
                }
            }
        }
        Console.WriteLine("ERR: No confirm received.");
        return false;
    }

    public static async Task SendConfirmIfNeeded(UdpClient client, byte[] buffer, IPEndPoint from)
    {
        MessageType type = (MessageType)buffer[0];
        ushort msgId = ReadMessageId(buffer);

        if (UdpState.IsDuplicate(msgId)) return;

        if (type is MessageType.REPLY or MessageType.MSG or MessageType.PING)
        {
            UdpState.MarkReceived(msgId);
            var confirm = new Confirm { RefMessageId = msgId };
            byte[] confirmBytes = confirm.ToBytes(0);
            await client.SendAsync(confirmBytes, confirmBytes.Length, from);
        }
    }

    public static ushort ReadMessageId(byte[] data)
    {
        if (data.Length < 3) return 0;
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
    }
}
}
