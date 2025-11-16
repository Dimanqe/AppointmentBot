using Telegram.Bot;

public class AdminBotClient
{
    public AdminBotClient(string token)
    {
        Client = new TelegramBotClient(token);
    }

    public ITelegramBotClient Client { get; }

    public long AdminChatId => 6959736008;
    public long AdminChatId2 => 5200461584;

    // ✅ Channel ID or username
    //public string NotificationChannel => "@AlashTestChannel";
    public string NotificationChannel => "@Alashcheb";

    public List<long> AdminChatIds => new()
    {
        AdminChatId,
        AdminChatId2
    };
}