#region

using AppointmentBot.Helpers;
using AppointmentBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

#endregion

namespace AppointmentBot.Controllers;

public class TextMessageController
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserSessionStorage _sessionStorage;

    public TextMessageController(UserBotClient botClient, IUserSessionStorage sessionStorage)
    {
        _botClient = botClient.Client;
        _sessionStorage = sessionStorage;
    }

    public async Task Handle(Message message, CancellationToken ct)
    {
        if (message == null || string.IsNullOrEmpty(message.Text))
            return;

        // For now, just show main menu on any message
        await ShowMainMenu(message, ct);
    }

    /// <summary>
    ///     Отображает главное меню при старте или при возврате.
    /// </summary>
    public async Task ShowMainMenu(Message message, CancellationToken ct)
    {
        if (message == null) return;

        var session = _sessionStorage.GetOrCreateSession(message.Chat.Id); // FIXED
        session.CurrentMenu = "main";
        session.MenuHistory.Clear();
        session.SelectedServices.Clear();
        session.SelectedTimeSlot = null;

        var buttons = MenuHelper.GetMainMenuButtons();

        var photoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "lash_logo.png");

        var caption =
            "<b>Добро пожаловать в A.lash 💖</b>\r\n\r\n" +
            "📍 Онлайн-запись к мастеру по наращиванию ресниц.\r\n\r\n" +
            "Telegram канал: <a href=\"https://t.me/Alashcheb\">A.lash</a>\r\n\r\n" +
            "Нажми на кнопку ниже, чтобы записаться 👇";

        try
        {
            if (File.Exists(photoPath))
            {
                await using var stream = File.OpenRead(photoPath);
                await _botClient.SendPhotoAsync(
                    message.Chat.Id,
                    new InputFileStream(stream),
                    caption: caption,
                    parseMode: ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(buttons),
                    cancellationToken: ct);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    caption + "\n\n(Изображение не найдено 📸)",
                    parseMode: ParseMode.Html,
                    disableWebPagePreview: true,
                    replyMarkup: new InlineKeyboardMarkup(buttons),
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error sending main menu: {ex.Message}");

            await _botClient.SendTextMessageAsync(
                message.Chat.Id,
                caption + "\n\n(Ошибка при отправке изображения 💬)",
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                replyMarkup: new InlineKeyboardMarkup(buttons),
                cancellationToken: ct);
        }
    }
}