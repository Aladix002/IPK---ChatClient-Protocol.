using System.Threading.Tasks;

namespace Transport
{
public interface IChatClient
{
    Task Run();
    Task Stop();
}
}