#region

using Telegram.Bot;

#endregion

namespace AppointmentBot.Clients;

public class UserBotClient
{
    public UserBotClient(string token)
    {
        Client = new TelegramBotClient(token);
    }

    public ITelegramBotClient Client { get; }
}