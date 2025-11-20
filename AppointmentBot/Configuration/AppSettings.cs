namespace AppointmentBot.Configuration;

public class AppSettings
{
    /// <summary>
    ///     Telegram API token for the user bot
    /// </summary>
    public string UserBotToken { get; set; }

    /// <summary>
    ///     Telegram API token for the admin bot
    /// </summary>
    public string AdminBotToken { get; set; }

    /// <summary>
    ///     PostgreSQL connection string
    /// </summary>
    public string PostgresConnection { get; set; }
    public string NotificationChannel { get; set; }
}