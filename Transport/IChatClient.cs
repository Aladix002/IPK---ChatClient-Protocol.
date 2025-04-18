using System.Threading.Tasks;

namespace Transport
{
public interface IChatClient
{
    Task RunAsync();
    Task DisconnectAsync();
}
}