using System.Collections.Generic;
using System.Threading;

namespace Transport
{
public static class UdpState
{
    private static ushort _messageId = 0;
    private static HashSet<ushort> _receivedIds = new();
    private static readonly Mutex _stateLock = new();
    private static State _state = State.start;
    public static string? UserDisplayName { get; set; }

    public static State GetState()
    {
        _stateLock.WaitOne();
        var state = _state;
        _stateLock.ReleaseMutex();
        return state;
    }

    public static void SetState(State state)
    {
        _stateLock.WaitOne();
        _state = state;
        _stateLock.ReleaseMutex();
    }

    public static ushort GetNextMessageId()
    {
        lock (_stateLock)
        {
            return _messageId++;
        }
    }

    public static void UpdateMessageIdIfNeeded(ushort receivedId)
    {
        lock (_stateLock)
        {
            if (receivedId >= _messageId)
            {
                _messageId = (ushort)(receivedId + 1);
            }
        }
    }

    public static bool IsDuplicate(ushort id)
    {
        return _receivedIds.Contains(id);
    }

    public static void MarkReceived(ushort id)
    {
        _receivedIds.Add(id);
    }
}
}
