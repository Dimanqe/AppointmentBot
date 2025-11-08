#region

using Telegram.Bot;

#endregion

namespace AppointmentBot;

public class AdminBotClient
{
    public AdminBotClient(string token)
    {
        Client = new TelegramBotClient(token);
    }

    public ITelegramBotClient Client { get; }

    public long AdminChatId => 6959736008;
    public long AdminChatId2 => 5200461584;

    public List<long> AdminChatIds => new()
    {
        AdminChatId,
        AdminChatId2
    };
}