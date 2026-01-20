namespace AetherGuard.Core.Services.Messaging;

public interface IMessageProducer
{
    void SendMessage<T>(T message);
}
