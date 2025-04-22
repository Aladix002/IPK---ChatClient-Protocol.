namespace Transport
{
public interface IChatClient
{
    Task Run();
    Task Stop();
}
}