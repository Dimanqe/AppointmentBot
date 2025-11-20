#region

using AppointmentBot.Clients;
using AppointmentBot.Helpers;
using AppointmentBot.Models;
using AppointmentBot.Repositories;
using AppointmentBot.Services;
using System.Globalization;
using System.Reflection.Emit;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static System.Runtime.InteropServices.JavaScript.JSType;

#endregion

namespace AppointmentBot.Controllers;

public class InlineKeyboardController
{
   

    private readonly ITelegramBotClient _botClient;
    private readonly IUserSessionStorage _sessionStorage;
    private readonly BotRepository _repository;
    private readonly TextMessageController _textMessageController; // ✅ add this
    private readonly CultureInfo _ruCulture = new("ru-RU");

    public InlineKeyboardController(
        UserBotClient botClient,
        IUserSessionStorage sessionStorage,
        BotRepository repository,
        TextMessageController textMessageController // ✅ add this
    )
    {
        _botClient = botClient.Client;
        _sessionStorage = sessionStorage;
        _repository = repository;
        _textMessageController = textMessageController; // ✅ store it
    }

    public async Task Handle(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var session = _sessionStorage.GetOrCreateSession(callbackQuery.From.Id);

        if (callbackQuery.Data == "ignore")
        {
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
            return;
        }

        if (callbackQuery?.Data == null || callbackQuery.Message == null)
            return;

        if (callbackQuery.Data.StartsWith("booking_"))
        {
            
            var bookingId = int.Parse(callbackQuery.Data.Replace("booking_", ""));
            var booking = await _repository.GetBookingByIdAsync(bookingId);
            if (booking == null)
                return;

            // Show booking detail with cancel option
            var services = string.Join(", ", booking.BookingServices.Select(bs => bs.Service.Name));
            var message = $"📅 {booking.Date:dd.MM.yyyy} ⏰ {booking.TimeSlot}\n" +
                          $"👩‍🎨 Мастер: {booking.Master.Name}\n" +
                          $"🧾 Услуги: {services}\n\n" +
                          "Вы можете отменить эту запись:";

            var buttons = new InlineKeyboardMarkup(new[]
            {
                CreateRow(CreateButton("❌ Отменить запись", $"cancel_booking_{booking.Id}")),
                CreateRow(CreateButton("⬅️ Назад", "my_bookings")),
                CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
            });

            await MenuMessage(callbackQuery, message, buttons, ct);
            return;
        }

        if (callbackQuery.Data.StartsWith("cancel_booking_"))
        {
            var bookingId = int.Parse(callbackQuery.Data.Replace("cancel_booking_", ""));
            var canceled = await _repository.CancelBookingAsync(bookingId);

            if (canceled)
            {
                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Запись отменена!",
                    cancellationToken: ct);
                await ShowUserBookings(callbackQuery, ct, session);
            }
            else
            {
                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "⚠️ Не удалось отменить запись.",
                    cancellationToken: ct);
            }
        }
        // -----------------------------
        //  Reminder actions
        // -----------------------------
        if (callbackQuery.Data.StartsWith("confirm_reminder") || callbackQuery.Data.StartsWith("cancel_reminder"))
        {
            int id = int.Parse(callbackQuery.Data.Replace("confirm_reminder", "").Replace("cancel_reminder", ""));
            var booking = await _repository.GetBookingByIdAsync(id);
            if (booking == null) return;

            string textToSend;

            if (callbackQuery.Data.StartsWith("confirm_reminder"))
            {
                booking.ReminderConfirmed = true;
                await _repository.UpdateBookingAsync(booking);

                textToSend = "Спасибо! Ваша запись подтверждена 👍";
            }
            else
            {
                bool canceled = await _repository.CancelReminderBookingAsync(id);
                if (!canceled) return;
                textToSend = "❌ Ваша запись отменена.";
            }

            // Edit message with new text and remove inline keyboard
            var sentMessage = await _botClient.EditMessageTextAsync(
                callbackQuery.Message.Chat.Id,
                callbackQuery.Message.MessageId,
                textToSend,
                parseMode: ParseMode.Html,
                replyMarkup: null
            );

            // Delete the message after 1 minute
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await _botClient.DeleteMessageAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId);
                }
                catch
                {
                    // Ignore if message already deleted or failed
                }
            });

            return;
        }


        switch (callbackQuery.Data)
        {
            case "Записаться":
                session.MenuHistory.Push(session.CurrentMenu);
                session.CurrentMenu = MenuStages.Services; // switch to services menu
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "⬅️ Назад":
            case "back":
                if (session.MenuHistory.Count > 0)
                {
                    session.CurrentMenu = session.MenuHistory.Pop();
                    await ShowMenu(callbackQuery, ct, session);
                }
                else
                {
                    session.CurrentMenu = MenuStages.Main;
                    await ShowMenu(callbackQuery, ct, session);
                }

                return;

            case "main_menu":
            case "Главное меню":
                session.CurrentMenu = MenuStages.Main;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;
            case "my_bookings":
                session.MenuHistory.Push(session.CurrentMenu);
                session.CurrentMenu = "my_bookings"; // add to MenuStages if needed
                _sessionStorage.SaveSession(session);
                await ShowUserBookings(callbackQuery, ct, session);
                return;

            case "finish_booking":
                if (!session.SelectedServices.Any())
                {
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                        "🚫 Пожалуйста, выберите хотя бы одну услугу!", cancellationToken: ct);
                    return;
                }

                session.MenuHistory.Push(session.CurrentMenu); // push Services
                session.CurrentMenu = MenuStages.Calendar;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "next_to_time":
                if (!session.SelectedDate.HasValue)
                {
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                        "🚫 Пожалуйста, выберите дату!", cancellationToken: ct);
                    return;
                }

                session.MenuHistory.Push(session.CurrentMenu);
                session.CurrentMenu = MenuStages.TimeSelection;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "prev_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(-1);
                session.CurrentMenu = MenuStages.Calendar;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "next_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(1);
                session.CurrentMenu = MenuStages.Calendar;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "next_to_confirm":
                if (!session.SelectedDate.HasValue || !session.SelectedTimeSlot.HasValue)
                {
                    await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                        "🚫 Пожалуйста, выберите время!", cancellationToken: ct);
                    return;
                }

                session.MenuHistory.Push(session.CurrentMenu);
                session.CurrentMenu = MenuStages.ConfirmationPrompt;
                _sessionStorage.SaveSession(session);
                await ShowMenu(callbackQuery, ct, session);
                return;

            case "confirm_booking":
            {
                var allServices = await _repository.GetAvailableServicesAsync();

                var serviceIds = session.SelectedServices
                    .Select(s => allServices.FirstOrDefault(db => db.Name == s)?.Id
                                 ?? throw new ArgumentException($"Unknown service: {s}"))
                    .ToList();

                if (!serviceIds.Any())
                {
                    await _botClient.AnswerCallbackQueryAsync(
                        callbackQuery.Id,
                        "🚫 Пожалуйста, выберите хотя бы одну услугу!",
                        cancellationToken: ct
                    );
                    return;
                }

                // Fetch user
                var user = await _repository.GetOrCreateUserAsync(callbackQuery.From);

                if (string.IsNullOrWhiteSpace(user.Phone))
                {
                    // Directly show ReplyKeyboard to request phone
                    var contactKeyboard = new ReplyKeyboardMarkup(new[]
                    {
                        KeyboardButton.WithRequestContact("📱 Отправить номер телефона")
                    })
                    {
                        ResizeKeyboard = true,
                        OneTimeKeyboard = true
                    };

                    await _botClient.SendTextMessageAsync(
                        callbackQuery.From.Id,
                        "📞 Пожалуйста, отправьте свой номер телефона для завершения записи:",
                        replyMarkup: contactKeyboard,
                        cancellationToken: ct
                    );

                    session.WaitingForPhone = true;
                    _sessionStorage.SaveSession(session);
                    return;
                }


                    // Already has phone, complete booking immediately
                    await CompleteBooking(callbackQuery, ct, session, serviceIds);
                return;
            }
            
            case "request_contact":
            {
                // Show Telegram's contact sharing keyboard
                var contactKeyboard = new ReplyKeyboardMarkup(new[]
                {
                    KeyboardButton.WithRequestContact("📱 Поделиться номером телефона")
                })
                {
                    ResizeKeyboard = true,
                    OneTimeKeyboard = true
                };

                await _botClient.SendTextMessageAsync(
                    callbackQuery.From.Id,
                    "Нажмите кнопку ниже, чтобы отправить номер:",
                    replyMarkup: contactKeyboard,
                    cancellationToken: ct
                );

                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);

                session.WaitingForPhone = true;
                _sessionStorage.SaveSession(session);
                return;
            }

            case "skip_phone":
            {
                var allServices = await _repository.GetAvailableServicesAsync();
                var serviceIds = session.SelectedServices
                    .Select(s => allServices.FirstOrDefault(db => db.Name == s)?.Id
                                 ?? throw new ArgumentException($"Unknown service: {s}"))
                    .ToList();

                await CompleteBooking(callbackQuery, ct, session, serviceIds);

                session.WaitingForPhone = false;
                _sessionStorage.SaveSession(session);

                // Remove the Telegram ReplyKeyboard
                var removeKeyboard = new ReplyKeyboardRemove();
                await _botClient.SendTextMessageAsync(
                    callbackQuery.From.Id,
                    "Пропустили отправку номера. Продолжаем запись...",
                    replyMarkup: removeKeyboard,
                    cancellationToken: ct
                );

                await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                return;
            }

            default:
                await HandleDynamicSelections(callbackQuery, ct, session);
                return;
        }

       

    }

    private async Task HandleDynamicSelections(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        // Services
        if (callbackQuery.Data.StartsWith("service_"))
        {
            if (!int.TryParse(callbackQuery.Data.Replace("service_", ""), out var serviceId))
                return;

            var service = await _repository.GetServiceByIdAsync(serviceId);
            if (service == null) return;

            var selectedServices = session.SelectedServicesJson == null
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(session.SelectedServicesJson)!;

            if (selectedServices.Contains(service.Name))
                selectedServices.Remove(service.Name);
            else
                selectedServices.Add(service.Name);

            session.SelectedServices = selectedServices;
            session.SelectedServicesJson = JsonSerializer.Serialize(selectedServices);

            session.CurrentMenu = MenuStages.Services;
            _sessionStorage.SaveSession(session);
            await ShowServiceMenu(callbackQuery, ct, session);
        }

        // Date selection
        if (callbackQuery.Data.StartsWith("date_"))
        {
            var selectedDate = DateTime.Parse(callbackQuery.Data.Replace("date_", ""));
            if (session.SelectedDate.HasValue && session.SelectedDate.Value.Date == selectedDate.Date)
                session.SelectedDate = null; // deselect
            else
                session.SelectedDate = selectedDate;

            _sessionStorage.SaveSession(session);
            await ShowMenu(callbackQuery, ct, session);
            return;
        }

        // Time selection
        if (callbackQuery.Data.StartsWith("time_"))
        {
            var parts = callbackQuery.Data.Replace("time_", "").Split('_');
            var date = DateTime.Parse(parts[0]);
            var time = TimeSpan.Parse(parts[1]); // parse HH:mm

            if (session.SelectedDate.HasValue &&
                session.SelectedDate.Value.Date == date.Date &&
                session.SelectedTimeSlot.HasValue &&
                session.SelectedTimeSlot.Value == time)
            {
                session.SelectedTimeSlot = null; // deselect if already selected
            }
            else
            {
                session.SelectedDate = date;
                session.SelectedTimeSlot = time; // store as TimeSpan
            }

            _sessionStorage.SaveSession(session);
            await ShowMenu(callbackQuery, ct, session); // refresh inline keyboard

         

            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id); // close spinner
        }

    }

    private async Task ShowMenu(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        switch (session.CurrentMenu)
        {
            case MenuStages.Main:
                await ShowMainMenu(callbackQuery, ct, session);
                break;
            case MenuStages.Services:
                await ShowServiceMenu(callbackQuery, ct, session);
                break;
            case MenuStages.Calendar:
                await ShowCalendarMenu(callbackQuery, ct, session);
                break;
            case MenuStages.TimeSelection:
                await ShowTimeMenu(callbackQuery, ct, session);
                break;
            case MenuStages.ConfirmationPrompt:
                await ShowBookingConfirmationPrompt(callbackQuery, ct, session);
                break;
            case MenuStages.ConfirmationDone:
                await ShowFinalConfirmation(callbackQuery, ct, session);
                break;
            case MenuStages.About:
                await ShowInfoMenu(callbackQuery, ct);
                break;
            default:
                await ShowMainMenu(callbackQuery, ct, session);
                break;
        }
    }

    // Menu stages
    private static class MenuStages
    {
        public const string Main = "main";
        public const string Services = "services";
        public const string Calendar = "calendar";
        public const string TimeSelection = "time_selection";
        public const string ConfirmationPrompt = "confirmation_prompt";
        public const string ConfirmationDone = "confirmation_done";
        public const string About = "about";
    }

    #region Menu Screens

    private async Task ShowMainMenu(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        // Clear the session for a fresh start
        session.SelectedServices.Clear();
        session.SelectedServicesJson = null;
        session.SelectedDate = null;
        session.SelectedTimeSlot = null;
        session.CurrentMonth = DateTime.Now;
        session.MenuHistory.Clear();
        session.CurrentMenu = MenuStages.Main;

        _sessionStorage.SaveSession(session);

        // Build main menu buttons
        var buttons = MenuHelper.GetMainMenuButtons().ToList();

        // Check if user has bookings
        var userBookings = await _repository.GetUserBookingsAsync(session.UserId);
        if (userBookings.Any()) buttons.Insert(0, CreateRow(CreateButton("📋 Мои записи", "my_bookings")));

        await MenuMessage(callbackQuery,
            "<b>Запись в A.lash онлайн 💖</b>\n\n" +
            "📣 Telegram канал: https://t.me/Alashcheb\n\n" +
            "Нажми на кнопку, чтобы записаться 👇",
            new InlineKeyboardMarkup(buttons),
            ct);
    }

    private async Task ShowUserBookings(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        var bookings = await _repository.GetUserBookingsAsync(session.UserId);

        if (!bookings.Any())
        {
            await MenuMessage(callbackQuery,
                "У вас пока нет записей.",
                new InlineKeyboardMarkup(new[]
                {
                    CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
                }),
                ct);
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();
        var messageText = "<b>Ваши записи:</b>\n\n";

        foreach (var booking in bookings)
        {
            var services = string.Join(", ", booking.BookingServices.Select(bs => bs.Service.Name));
            messageText +=
                $"📅 {booking.Date:dd.MM.yyyy} ⏰ {booking.TimeSlot}\n" +
                $"👩‍🎨 Мастер: {booking.Master.Name}\n" +
                $"🧾 Услуги: {services}\n\n";

            // Each booking gets a button with its ID as callback
            buttons.Add(CreateRow(CreateButton(
                $"{booking.Date:dd.MM.yyyy} {booking.TimeSlot} - {booking.Master.Name}",
                $"booking_{booking.Id}")));
        }

        // Add back to main menu button
        buttons.Add(CreateRow(CreateButton("🏠 Главное меню", "main_menu")));

        await MenuMessage(callbackQuery, messageText, new InlineKeyboardMarkup(buttons), ct);
    }

    private async Task ShowServiceMenu(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        session.CurrentMenu = MenuStages.Services;

        var selected = session.SelectedServicesJson == null
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(session.SelectedServicesJson)!;

        // Fetch services from DB
        var dbServices = await _repository.GetAvailableServicesAsync();

        var buttons = dbServices.Select(s =>
            CreateRow(CreateButton($"{s.Name} — {s.Price}₽", $"service_{s.Id}", selected.Contains(s.Name)))
        ).ToList();

        // Await the async method
        var info = await FormatBookingInfoAsync(session);

        var nextLabel = selected.Any() ? "Далее ➡️" : "🚫 Далее ➡️";

        buttons.Add(CreateRow(CreateButton("⬅️ Назад", "back"), CreateButton(nextLabel, "finish_booking")));
        buttons.Add(CreateRow(CreateButton("🏠 Главное меню", "main_menu")));

        await MenuMessage(callbackQuery, $"{info}\n\n<b>Выберите необходимые услуги</b>",
            new InlineKeyboardMarkup(buttons), ct);
    }

    private async Task ShowCalendarMenu(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        session.CurrentMenu = MenuStages.Calendar;

        var a = $"{session.CurrentMonth:MMMM yyyy}";
        Console.WriteLine(a);

    var buttons = await BuildCalendarAsync(session.CurrentMonth, session); // await here
        await MenuMessage(callbackQuery, $"<b>Выберите дату:</b>\n\n{session.CurrentMonth.ToString("MMMM yyyy",_ruCulture) }", buttons, ct);


    }

    private async Task ShowTimeMenu(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        session.CurrentMenu = MenuStages.TimeSelection;

        if (!session.SelectedDate.HasValue)
        {
            await _botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                "🚫 Сначала выберите дату!", cancellationToken: ct);
            return;
        }

        await ShowTimePickerAsync(callbackQuery, session.SelectedDate.Value, session, ct);
    }

    private async Task ShowBookingConfirmationPrompt(CallbackQuery callbackQuery, CancellationToken ct,
        UserSession session)
    {
        session.CurrentMenu = MenuStages.ConfirmationPrompt;
        var services = session.SelectedServicesJson == null
            ? "не выбрано"
            : string.Join(", ", JsonSerializer.Deserialize<List<string>>(session.SelectedServicesJson)!);
        var message = await FormatBookingInfoAsync(session) + "\n<b>Проверьте информацию...</b>";

        var buttons = new InlineKeyboardMarkup(new[]
        {
            CreateRow(CreateButton("✅ Подтвердить", "confirm_booking")),
            CreateRow(CreateButton("⬅️ Назад", "back")),
            CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
        });

        await MenuMessage(callbackQuery, message, buttons, ct);
    }


    private async Task ShowFinalConfirmation(CallbackQuery callbackQuery, CancellationToken ct, UserSession session)
    {
        session.CurrentMenu = MenuStages.ConfirmationDone;
        var message = await FormatBookingInfoAsync(session) +
                      "\nСпасибо за запись! Мы свяжемся с вами для подтверждения 💖";

        var buttons = new InlineKeyboardMarkup(new[]
        {
            CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
        });

        await MenuMessage(callbackQuery, message, buttons, ct);
    }
    private async Task ShowInfoMenu(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var buttons = new InlineKeyboardMarkup(new[]
        {
            CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
        });

        await MenuMessage(callbackQuery,
            "<b>О нас</b>\n\n" +
            "Мы — студия <b>A.lash</b>, специализируемся на профессиональном наращивании и уходе за ресницами 💖\n\n" +
            "Наш мастер Арина подберёт идеальный образ для вас.",
            buttons,
            ct);
    }

    #endregion

    #region Helpers

    private InlineKeyboardButton CreateButton(string text, string callbackData, bool selected = false)
    {
        if (selected) text = "✅ " + text;
        return InlineKeyboardButton.WithCallbackData(text, callbackData);
    }

    private InlineKeyboardButton[] CreateRow(params InlineKeyboardButton[] buttons)
    {
        return buttons;
    }

    // Updated async version of FormatBookingInfo
    private async Task<string> FormatBookingInfoAsync(UserSession session)
    {
        var studio = await _repository.GetStudioAsync();
        // Calculate total duration and cost from DB
        var (totalDuration, totalCost) = await CalculateBookingSummaryAsync(session);

        var services = session.SelectedServices.Any()
            ? Environment.NewLine + string.Join("\n", session.SelectedServices)
            : "не выбрано";

        var date = session.SelectedDate?.ToString("dd.MM.yyyy") ?? "не выбрано";
        var time = session.SelectedTimeSlot.HasValue
            ? session.SelectedTimeSlot.Value.ToString(@"hh\:mm")
            : "не выбрано";

        return
            $"💖 <b>Информация о записи</b>\n\n" +
            $"🏠 Студия: {studio.Name}\n" +
            $"👩‍🎨 Мастер: Арина\n" +
            $"📍 Адрес: {studio.Address}\n" +
            $"⏱️ Продолжительность: {totalDuration.Hours} ч. {totalDuration.Minutes} м.\n" +
            $"💰 Стоимость: {totalCost}₽\n\n" +
            $"🧾 Услуги: {services}\n" +
            $"📅 Дата: {date}\n" +
            $"⏰ Время: {time}\n";
    }

    // Updated async version of CalculateBookingSummary
    private async Task<(TimeSpan totalDuration, int totalCost)> CalculateBookingSummaryAsync(UserSession session)
    {
        if (session.SelectedServices.Count == 0)
            return (TimeSpan.Zero, 0);

        // Fetch all active services from DB
        var allServices = await _repository.GetAvailableServicesAsync();

        // Match selected services by name
        var selectedServices = allServices
            .Where(s => session.SelectedServices.Contains(s.Name))
            .ToList();

        // Calculate total duration and cost
        var totalDuration =
            TimeSpan.FromMinutes(selectedServices.Sum(s =>
                s.DurationMinutes)); // assumes Service.DurationMinutes exists
        var totalCost = selectedServices.Sum(s => s.Price);

        return (totalDuration, totalCost);
    }


    public async Task MenuMessage(CallbackQuery callbackQuery, string text, InlineKeyboardMarkup replyMarkup,
        CancellationToken ct)
    {
        try
        {
            if (callbackQuery.Message != null)
            {
                var sameText = callbackQuery.Message.Text == text;
                var sameCaption = callbackQuery.Message.Caption == text;
                var currentMarkup = callbackQuery.Message.ReplyMarkup?.ToString() ?? "";
                var newMarkup = replyMarkup?.ToString() ?? "";
                var sameMarkup = currentMarkup == newMarkup;

                if ((sameText || sameCaption) && sameMarkup)
                    return;
            }

            if (callbackQuery.Message?.Photo != null && callbackQuery.Message.Photo.Any())
                await _botClient.EditMessageCaptionAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    text,
                    ParseMode.Html,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
            else if (callbackQuery.Message != null)
                await _botClient.EditMessageTextAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    text,
                    ParseMode.Html,
                    disableWebPagePreview: true,
                    replyMarkup: replyMarkup,
                    cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            if (!ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"Telegram API Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General Error editing message: {ex.Message}");
        }
    }

    #endregion

    #region Calendar & Time Picker

    private async Task<InlineKeyboardMarkup> BuildCalendarAsync(DateTime month, UserSession session)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        // Month navigation
        buttons.Add(CreateRow(
            CreateButton(month > DateTime.Today ? "⬅️" : " ", "prev_month"),
            CreateButton($"{month:MMMM yyyy}", "ignore"),
            CreateButton("➡️", "next_month")
        ));

        // Weekday headers
        buttons.Add(CreateRow(
            CreateButton("Пн", "ignore"),
            CreateButton("Вт", "ignore"),
            CreateButton("Ср", "ignore"),
            CreateButton("Чт", "ignore"),
            CreateButton("Пт", "ignore"),
            CreateButton("Сб", "ignore"),
            CreateButton("Вс", "ignore")
        ));

        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var firstDayOfWeek = ((int)new DateTime(month.Year, month.Month, 1).DayOfWeek + 6) % 7 + 1; // Monday=1
        var dayCounter = 1;

        // Fetch all active slots for the month
        var startOfMonth = new DateTime(month.Year, month.Month, 1);
        var endOfMonth = new DateTime(month.Year, month.Month, daysInMonth);
        var allSlots = await _repository.GetActiveTimeSlotsForRangeAsync(startOfMonth, endOfMonth);

        for (var week = 0; week < 6; week++)
        {
            var row = new List<InlineKeyboardButton>();
            for (var dow = 1; dow <= 7; dow++)
                if ((week == 0 && dow < firstDayOfWeek) || dayCounter > daysInMonth)
                {
                    row.Add(CreateButton(" ", "ignore"));
                }
                else
                {
                    var date = new DateTime(month.Year, month.Month, dayCounter);

                    // Only keep slots for this date
                    var slotsForDate = allSlots.Where(ts => ts.Date.Date == date.Date).ToList();

                    if (!slotsForDate.Any() || date < DateTime.Today)
                    {
                        // No available slots or past day
                        row.Add(CreateButton( $"🚫{dayCounter.ToString()}", "ignore"));
                    }
                    else
                    {
                        var label = dayCounter.ToString();
                        var callbackData = $"date_{date:yyyy-MM-dd}";

                        if (session.SelectedDate.HasValue && session.SelectedDate.Value.Date == date.Date)
                            label = $"✅ {label}";

                        row.Add(CreateButton(label, callbackData));
                    }

                    dayCounter++;
                }

            buttons.Add(row.ToArray());
        }

        // Navigation buttons
        var nextLabel = session.SelectedDate.HasValue ? "Далее ➡️" : "🚫 Далее ➡️";
        buttons.Add(CreateRow(CreateButton("⬅️ Назад", "back"), CreateButton(nextLabel, "next_to_time")));
        buttons.Add(CreateRow(CreateButton("🏠 Главное меню", "main_menu")));

        return new InlineKeyboardMarkup(buttons);
    }

    private async Task ShowTimePickerAsync(CallbackQuery callbackQuery, DateTime date, UserSession session,
        CancellationToken ct)
    {
        var slots = await GetAvailableTimeSlotsForDateFromDbAsync(date);

        if (!slots.Any())
        {
            await MenuMessage(callbackQuery,
                $"На {date:dd.MM.yyyy} нет доступных окон. Выберите другой день.",
                new InlineKeyboardMarkup(new[]
                {
                    CreateRow(CreateButton("⬅️ Назад", "back")),
                    CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
                }),
                ct);
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();

        for (var i = 0; i < slots.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>();
            for (var j = i; j < i + 2 && j < slots.Count; j++)
            {
                var label = slots[j].ToString(@"hh\:mm");
                if (session.SelectedTimeSlot.HasValue && session.SelectedTimeSlot.Value == slots[j])
                    label = $"✅ {label}";

                row.Add(CreateButton(label, $"time_{date:yyyy-MM-dd}_{slots[j]:hh\\:mm}"));
            }

            buttons.Add(row.ToArray());
        }

        var nextLabel = session.SelectedTimeSlot.HasValue ? "Далее ➡️" : "🚫 Далее ➡️";
        buttons.Add(CreateRow(CreateButton("⬅️ Назад", "back"), CreateButton(nextLabel, "next_to_confirm")));
        buttons.Add(CreateRow(CreateButton("🏠 Главное меню", "main_menu")));

        await MenuMessage(callbackQuery,
            $"<b>Выберите время на {date:dd.MM.yyyy}:</b>",
            new InlineKeyboardMarkup(buttons),
            ct);
    }

    private async Task ShowTimePicker(CallbackQuery callbackQuery, DateTime date, CancellationToken ct,
        UserSession session)
    {
        //var slots = await GetAvailableTimeSlotsFromDbAsync(date);

        //if (!slots.Any())
        //{
        //    await MenuMessage(callbackQuery,
        //        $"На {date:dd.MM.yyyy} нет доступных окон. Пожалуйста, выберите другой день.",
        //        new InlineKeyboardMarkup(new[] {
        //            CreateRow(CreateButton("⬅️ Назад", "back")),
        //            CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
        //        }),
        //        ct);
        //    return;
        //}
        var slots = await GetAvailableTimeSlotsForDateFromDbAsync(date);

        if (!slots.Any())
        {
            await MenuMessage(callbackQuery,
                $"На {date:dd.MM.yyyy} нет доступных окон. Пожалуйста, выберите другой день.",
                new InlineKeyboardMarkup(new[]
                {
                    CreateRow(CreateButton("⬅️ Назад", "back")),
                    CreateRow(CreateButton("🏠 Главное меню", "main_menu"))
                }),
                ct);
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();

        for (var i = 0; i < slots.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>();
            for (var j = i; j < i + 2 && j < slots.Count; j++)
            {
                var displayTime = slots[j].ToString(@"hh\:mm");
                if (session.SelectedTimeSlot.HasValue
                    && session.SelectedTimeSlot.Value == slots[j]
                    && session.SelectedDate.HasValue
                    && session.SelectedDate.Value.Date == date.Date)
                    displayTime = $"✅ {displayTime}";

                row.Add(CreateButton(displayTime, $"time_{date:yyyy-MM-dd}_{slots[j]:hh\\:mm}"));
            }

            buttons.Add(row.ToArray());
        }

        var nextLabel = session.SelectedTimeSlot.HasValue ? "Далее ➡️" : "🚫 Далее ➡️";

        buttons.Add(CreateRow(CreateButton("⬅️ Назад", "back"), CreateButton(nextLabel, "next_to_confirm")));
        buttons.Add(CreateRow(CreateButton("🏠 Главное меню", "main_menu")));

        await MenuMessage(callbackQuery, $"<b>Выберите время на {date:dd.MM.yyyy}:</b>",
            new InlineKeyboardMarkup(buttons), ct);
    }

    private async Task<List<TimeSpan>> GetAvailableTimeSlotsFromDbAsync(DateTime date)
    {
        // Get all active slots for the day of week
        var slots = await _repository.GetActiveTimeSlotsAsync(date.DayOfWeek);

        // Filter out past slots if date is today
        var availableSlots = slots
            .Select(ts => ts.StartTime)
            .Where(t => !(date.Date == DateTime.Today && t <= DateTime.Now.TimeOfDay))
            .OrderBy(t => t)
            .ToList();

        return availableSlots;
    }

    private async Task<List<TimeSpan>> GetAvailableTimeSlotsForDateFromDbAsync(DateTime date)
    {
        var slots = await _repository.GetActiveTimeSlotsForDayAsync(date);

        // Filter out past times if today
        var availableSlots = slots
            .Select(ts => ts.StartTime)
            .Where(t => !(date.Date == DateTime.Today && t <= DateTime.Now.TimeOfDay))
            .OrderBy(t => t)
            .ToList();

        return availableSlots;
    }
    public async Task CompleteBooking(CallbackQuery callbackQuery, CancellationToken ct, UserSession session, List<int> serviceIds)
    {
        await _repository.AddBookingAsync(
            session.UserId,
            1,
            serviceIds,
            session.SelectedDate!.Value,
            session.SelectedTimeSlot!.Value,
            callbackQuery.From
        );

        session.MenuHistory.Push(session.CurrentMenu);
        session.CurrentMenu = MenuStages.ConfirmationDone;
        _sessionStorage.SaveSession(session);
        await ShowMenu(callbackQuery, ct, session);
    }

    #endregion
}