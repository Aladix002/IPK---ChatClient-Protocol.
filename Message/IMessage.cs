namespace Message;

public interface IMessage
{
    MessageType MessageType { get; }
    byte[] ToBytes(ushort messageId);
}

public interface ITcpMessage : IMessage
{
    string ToTcpString();
}