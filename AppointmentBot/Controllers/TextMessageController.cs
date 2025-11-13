using AppointmentBot;
using AppointmentBot.Helpers;
using AppointmentBot.Models;
using AppointmentBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class TextMessageController
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserSessionStorage _sessionStorage;
    private readonly BotRepository _repository;

    public TextMessageController(UserBotClient botClient, IUserSessionStorage sessionStorage, BotRepository repository)
    {
        _botClient = botClient.Client;
        _sessionStorage = sessionStorage;
        _repository = repository;
    }

    public async Task Handle(Message message, CancellationToken ct)
    {
        if (message == null) return;

        var session = _sessionStorage.GetOrCreateSession(message.Chat.Id);

        // 1️⃣ Handle shared contact
        if (message.Contact != null && session.WaitingForPhone)
        {
            var phone = message.Contact.PhoneNumber;

            // Get or create user
            var user = await _repository.GetOrCreateUserAsync(message.From);
            user.Phone = phone;

            // Update phone in DB
            await _repository.UpdateUserPhoneAsync(user.Id, user.Phone);

            // Clear waiting flag
            session.WaitingForPhone = false;
            _sessionStorage.SaveSession(session);

            // Remove ReplyKeyboard
            await _botClient.SendTextMessageAsync(
                message.Chat.Id,
                "✅ Спасибо! Ваш номер успешно сохранен.",
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct
            );

            // Complete booking
            var allServices = await _repository.GetAvailableServicesAsync();
            var serviceIds = session.SelectedServices
                .Select(s => allServices.FirstOrDefault(db => db.Name == s)?.Id
                             ?? throw new ArgumentException($"Unknown service: {s}"))
                .ToList();

            await CompleteBookingForMessage(message, session, serviceIds, ct);
            return;
        }


        // 2️⃣ Optional: handle if user did not share contact
        if (session.WaitingForPhone)
        {
            session.WaitingForPhone = false;
            _sessionStorage.SaveSession(session);
        }

        // 3️⃣ Default: show main menu
        await ShowMainMenu(message, ct);
    }


    public async Task CompleteBookingForMessage(Message message, UserSession session, List<int> serviceIds, CancellationToken ct)
    {
        // Add booking
        await _repository.AddBookingAsync(
            session.UserId,
            1, // masterId, adjust if needed
            serviceIds,
            session.SelectedDate!.Value,
            session.SelectedTimeSlot!.Value,
            message.From
        );

        session.CurrentMenu = "confirmation_done";
        _sessionStorage.SaveSession(session);

        // Format booking info
        var bookingInfo = await FormatBookingInfoAsync(session);

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "main_menu") }
        });

        await _botClient.SendTextMessageAsync(
            message.Chat.Id,
            bookingInfo + "\n💖 Спасибо за запись! Мы свяжемся с вами для подтверждения.",
            parseMode: ParseMode.Html,
            replyMarkup: buttons,
            cancellationToken: ct
        );
    }

    private async Task<string> FormatBookingInfoAsync(UserSession session)
    {
        var allServices = await _repository.GetAvailableServicesAsync();
        var selectedServices = allServices.Where(s => session.SelectedServices.Contains(s.Name)).ToList();

        var totalDuration = TimeSpan.FromMinutes(selectedServices.Sum(s => s.DurationMinutes));
        var totalCost = selectedServices.Sum(s => s.Price);

        var servicesText = selectedServices.Any()
            ? string.Join("\n", selectedServices.Select(s => s.Name))
            : "не выбрано";

        var date = session.SelectedDate?.ToString("dd.MM.yyyy") ?? "не выбрано";
        var time = session.SelectedTimeSlot?.ToString(@"hh\:mm") ?? "не выбрано";

        return
            "💖 <b>Информация о записи</b>\n\n" +
            "📍 Студия: A.lash\n" +
            "👩‍🎨 Мастер: Арина\n" +
            "🏠 Адрес: онлайн\n\n" +
            $"⏱️ Продолжительность: {totalDuration.Hours} ч. {totalDuration.Minutes} м.\n" +
            $"💰 Стоимость: {totalCost}₽\n\n" +
            $"🧾 Услуги:\n{servicesText}\n" +
            $"📅 Дата: {date}\n" +
            $"⏰ Время: {time}\n";
    }

    public async Task AskForPhoneNumber(long chatId, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            KeyboardButton.WithRequestContact("📱 Поделиться номером телефона")
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId,
            "📞 Пожалуйста, поделитесь вашим номером телефона, нажав кнопку ниже:",
            replyMarkup: keyboard,
            cancellationToken: ct
        );
    }

    public async Task ShowMainMenu(Message message, CancellationToken ct)
    {
        if (message == null) return;

        var session = _sessionStorage.GetOrCreateSession(message.Chat.Id);
        session.CurrentMenu = "main";
        session.MenuHistory.Clear();
        session.SelectedServices.Clear();
        session.SelectedTimeSlot = null;
        _sessionStorage.SaveSession(session);

        var buttons = MenuHelper.GetMainMenuButtons().ToList();

        var userBookings = await _repository.GetUserBookingsAsync(session.UserId);
        if (userBookings.Any())
            buttons.Insert(0, new[] { InlineKeyboardButton.WithCallbackData("📋 Мои записи", "my_bookings") });

        var caption = "<b>Добро пожаловать в A.lash 💖</b>\n\n" +
                      "📍 Онлайн-запись к мастеру по наращиванию ресниц.\n\n" +
                      "Telegram канал: <a href=\"https://t.me/Alashcheb\">A.lash</a>\n\n" +
                      "Нажми на кнопку ниже, чтобы записаться 👇";

        await _botClient.SendTextMessageAsync(
            message.Chat.Id,
            caption,
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct
        );
    }
}
