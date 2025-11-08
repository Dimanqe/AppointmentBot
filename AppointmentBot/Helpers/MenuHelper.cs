#region

using Telegram.Bot.Types.ReplyMarkups;

#endregion

namespace AppointmentBot.Helpers;

public static class MenuHelper
{
    public static List<InlineKeyboardButton[]> GetMainMenuButtons()
    {
        return new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("Записаться👤", "Записаться") }
        };
    }
}