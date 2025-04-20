using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Message;

namespace Transport
{
    //retry a potvrdenia
    public static class UdpConfirmHelper
    {
        private static readonly ConcurrentDictionary<ushort, bool> Confirmed = new();

        public static async Task<bool> SendWithConfirm(UdpClient c, byte[] msg, IPEndPoint dst, ushort id, Arguments a)
        {
            Confirmed.TryRemove(id, out _);

            for (int attempt = 1; attempt <= a.MaxRetries; attempt++)
            {
                await c.SendAsync(msg, msg.Length, dst);
                var start = DateTime.UtcNow;
                while ((DateTime.UtcNow - start).TotalMilliseconds < a.UdpTimeout)
                {
                    if (Confirmed.ContainsKey(id)) return true;
                    await Task.Delay(a.UdpTimeout);
                }
            }
            Console.WriteLine($"ERR: No confirm for {id}");
            return false;
        }

        public static async Task SendConfirmIfNeeded(UdpClient c, byte[] buf, IPEndPoint from)
        {
            MessageType t = (MessageType)buf[0];
            ushort id = ReadMessageId(buf);

            if (t == MessageType.CONFIRM)
            {
                var conf = Confirm.FromBytes(buf);
                Confirmed[conf.RefMessageId] = true; // uklada ze prisiel conf
                return;
            }

            if (UdpStateManager.IsDuplicate(id)) return; 

            if (t is MessageType.REPLY or MessageType.MSG or MessageType.PING or MessageType.ERR)
            {
                UdpStateManager.MarkReceived(id);
                // Posli CONFIRM
                var conf = new Confirm { RefMessageId = id };
                await c.SendAsync(conf.ToBytes(0), 3, from);
            }
        }

        //vybera ID z bytov
        public static ushort ReadMessageId(byte[] d)
        {
            if (d.Length < 3) return 0;
            return BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(1, 2));
        }
    }
}

