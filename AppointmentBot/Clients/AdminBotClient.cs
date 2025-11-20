using Telegram.Bot;

public class AdminBotClient
{
    public AdminBotClient(string token, string notificationChannel)
    {
        Client = new TelegramBotClient(token);
        _notificationChannel = notificationChannel; }

    public ITelegramBotClient Client { get; }

    private readonly string _notificationChannel;    

    public long AdminChatId => 6959736008;
    public long AdminChatId2 => 5200461584;

    // ✅ Channel ID or username
    public string NotificationChannel => _notificationChannel;

    public List<long> AdminChatIds => new()
    {
        AdminChatId,
        AdminChatId2
    };

    public List<long> AdminChatIdsForChannelMessageUpdate => new()
    {
        AdminChatId,
        //AdminChatId2
    };
}