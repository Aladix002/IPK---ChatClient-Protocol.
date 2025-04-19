using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public static class UdpConfirmHelper
{
    private static readonly ConcurrentDictionary<ushort, bool> ConfirmedMessages = new();

    public static async Task<bool> SendWithConfirm(UdpClient client, byte[] message, IPEndPoint target, ushort msgId, Arguments args)
    {
        ConfirmedMessages.TryRemove(msgId, out _); 
        
        for (int attempt = 1; attempt <= args.MaxRetries; attempt++)
        {
            await client.SendAsync(message, message.Length, target);
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalMilliseconds < args.UdpTimeout)
            {
                if (ConfirmedMessages.ContainsKey(msgId))
                {
                    return true;
                }
                await Task.Delay(10);
            }
        }
        Console.WriteLine($"ERR: No confirm received for ID={msgId}.");
        return false;
    }

    public static async Task SendConfirmIfNeeded(UdpClient client, byte[] buffer, IPEndPoint from)
    {
        MessageType type = (MessageType)buffer[0];
        ushort msgId = ReadMessageId(buffer);

        if (type == MessageType.CONFIRM)
        {
            var confirm = Confirm.FromBytes(buffer);
            ConfirmedMessages[confirm.RefMessageId] = true;
            return;
        }

        if (UdpStateManager.IsDuplicate(msgId)) return;

        if (type is MessageType.REPLY or MessageType.MSG or MessageType.PING or MessageType.ERR)
        {
            UdpStateManager.MarkReceived(msgId);

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
