using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Message;

namespace Transport
{
    public class TcpStateManager
    {
        private readonly Mutex _stateLock = new();    
        private State _state = State.start;       
        private string? _userDisplayName;          
        private bool _shutdownInitiated = false;   

        public void SetState(State s)
        {
            _stateLock.WaitOne();
            _state = s;
            _stateLock.ReleaseMutex();
        }

        public State GetState()
        {
            _stateLock.WaitOne();
            var s = _state;
            _stateLock.ReleaseMutex();
            return s;
        }

        public string? DisplayName
        {
            get => _userDisplayName;
            set => _userDisplayName = value;
        }

        //Posle BYE a zavrie socket
        public async Task Stop(Socket socket)
        {
            if (_shutdownInitiated) return;     
            _shutdownInitiated = true;

            try
            {
                var bye = new TcpMessage
                {
                    Type = MessageType.BYE,
                    DisplayName = _userDisplayName ?? "?"
                };
                var data = Encoding.ASCII.GetBytes(bye.ToTcpString());
                await socket.SendAsync(data, SocketFlags.None); 
            }
            catch {}

            socket.Close();//zavre spojenie
        }

        public async Task SendErrorAndExit(Socket socket, string message)
        {
            try
            {
                var err = new TcpMessage
                {
                    Type = MessageType.ERR,
                    DisplayName = _userDisplayName ?? "client",
                    MessageContents = message
                };
                await socket.SendAsync(Encoding.ASCII.GetBytes(err.ToTcpString()), SocketFlags.None);
            }
            catch { }

            await Stop(socket);
            Environment.Exit(1);
        }
    }
}

