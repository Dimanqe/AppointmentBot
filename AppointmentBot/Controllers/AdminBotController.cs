#region

using AppointmentBot.Models;
using Microsoft.Extensions.Options;
using System.Globalization;
using AppointmentBot.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using AppointmentBot.Repositories;
using AppointmentBot.Storage;
using AppointmentBot.Storage.Models;

#endregion

namespace AppointmentBot.Controllers;

public class AdminBotController
{
    private readonly ITelegramBotClient _adminBotClient;
    private readonly IAdminSessionStorage _adminSessionStorage;
    private readonly AdminRepository _repository;
    private readonly CultureInfo _ruCulture = new("ru-RU");
    private readonly List<long> _admins;

    public AdminBotController(
        AdminBotClient adminBotClient,
        AdminRepository repository,
        IAdminSessionStorage adminSessionStorage)
    {
        _adminBotClient = adminBotClient.Client;
        _repository = repository;
        _adminSessionStorage = adminSessionStorage;

        _admins = adminBotClient.AdminChatIds;
    }


    #region CallbackQuery Handling

    public async Task Handle(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (!_admins.Contains(callbackQuery.From.Id))
        {
            await _adminBotClient.AnswerCallbackQueryAsync(
                callbackQuery.Id,
                "⛔ У вас нет доступа."
            );

            // Optional: delete button message
            try
            {
                await _adminBotClient.DeleteMessageAsync(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId
                );
            }
            catch { }

            return;
        }

        if (callbackQuery?.Data == null || callbackQuery.Message == null) return;

        var chatId = callbackQuery.Message.Chat.Id;
        var adminId = callbackQuery.From.Id;
        var session = _adminSessionStorage.GetOrCreateSession(adminId);

       var replyMarkup = new InlineKeyboardMarkup(new[]
        {
            //new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_settings") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") }
        });
       

        switch (callbackQuery.Data)
        {
            case "admin_main":
                await ShowAdminMainMenu(callbackQuery);
                return;
            case "admin_settings":
                await ShowStudioSettings(chatId, callbackQuery.Message.MessageId);
                return;
            case "edit_studio_name":
                session.ActionType = "edit_studio_name";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новое название студии:",replyMarkup: replyMarkup);
                return;

            case "edit_studio_address":
                session.ActionType = "edit_studio_address";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId,session.LastBotMessageId, "Введите новый адрес студии:", replyMarkup: replyMarkup);
                return;

            case "edit_studio_phone":
                session.ActionType = "edit_studio_phone";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новый телефон студии:", replyMarkup: replyMarkup);
                return;

            case "edit_studio_telegram":
                session.ActionType = "edit_studio_telegram";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новый Telegram студии:", replyMarkup: replyMarkup);
                return;
            case "edit_studio_instagram":
                session.ActionType = "edit_studio_instagram";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новый Instagram студии:", replyMarkup: replyMarkup);
                return;

            case "edit_studio_description":
                session.ActionType = "edit_studio_description";
                session.LastBotMessageId = callbackQuery.Message.MessageId;
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новое описание студии:", replyMarkup: replyMarkup);
                return;

            case "admin_services":
                await ShowServices(chatId, callbackQuery.Message.MessageId);
                return;
            case "admin_add_service":
                _adminSessionStorage.SetSelectedService(adminId, -1);
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите имя новой услуги:", replyMarkup: replyMarkup);
                return;
            case "admin_bookings":
                await ShowAllBookings(callbackQuery);
                return;
            case "show_timeslots":
                await ShowTimeSlotsAdminCalendar(callbackQuery, session);
                return;
            case "send_all_slots":
                await _repository.SendAllFreeSlotsAsync(callbackQuery.Message.Chat.Id);
                break;
            case "prev_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(-1);
                _adminSessionStorage.SaveSession(session);
                await ShowAdminCalendar(callbackQuery, session);
                return;
            case "next_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(1);
                _adminSessionStorage.SaveSession(session);
                await ShowAdminCalendar(callbackQuery, session);
                return;
        }

        // ------------------ Existing service handling ------------------ //

        if (callbackQuery.Data.StartsWith("service_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("service_", ""));
            await ShowServiceOptions(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, serviceId);
            return;
        }

        if (callbackQuery.Data.StartsWith("edit_price_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("edit_price_", ""));
         

            var replyMarkupEditPrice = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"service_{serviceId}")},
                new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") }
            });
            session.TempServiceId = serviceId;
            session.ActionType = "price";
            session.LastBotMessageId = callbackQuery.Message.MessageId;
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новую цену услуги:", replyMarkup: replyMarkupEditPrice);
            return;
        }

        if (callbackQuery.Data.StartsWith("edit_duration_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("edit_duration_", ""));
            var replyMarkupEditDuration = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"service_{serviceId}")},
                new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") }
            });
            session.TempServiceId = serviceId;
            session.ActionType = "duration";
            session.LastBotMessageId = callbackQuery.Message.MessageId;
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId, "Введите новую продолжительность услуги (в минутах):", replyMarkup: replyMarkupEditDuration);
            return;
        }

        if (callbackQuery.Data.StartsWith("delete_service_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("delete_service_", ""));
            await _repository.DeleteServiceAsync(serviceId);
            await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Услуга удалена");
            await ShowServices(chatId, callbackQuery.Message.MessageId);
            return;
        }

        // ------------------ Booking handling ------------------ //

        if (callbackQuery.Data.StartsWith("booking_"))
        {
            var bookingId = int.Parse(callbackQuery.Data.Replace("booking_", ""));
            await ShowBookingDetails(callbackQuery, bookingId);
            return;
        }

        if (callbackQuery.Data.StartsWith("cancel_booking_"))
        {
            var bookingId = int.Parse(callbackQuery.Data.Replace("cancel_booking_", ""));
            await CancelBooking(callbackQuery, bookingId);
            return;
        }

        // ------------------ Time slot handling ------------------ //

        if (callbackQuery.Data == "add_timeslot")
        {
            await ShowAdminTimePicker(chatId, session, callbackQuery.Message.MessageId);
            return;
        }

        if (callbackQuery.Data.StartsWith("admin_date_"))
        {
            Console.WriteLine($"Callback: {callbackQuery.Data}");
            var dateStr = callbackQuery.Data.Replace("admin_date_", "");
            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var date))
            {
                session.TempSlotDate = date;
                _adminSessionStorage.SaveSession(session);

                // Pass the message ID to edit the same message
                await ShowAdminTimePicker(chatId, session, callbackQuery.Message.MessageId);
            }
            else
            {
                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚫 Ошибка при выборе даты.");
            }

            return;
        }
        if (callbackQuery.Data.StartsWith("admin_slot_date_"))
        {
            Console.WriteLine($"Callback: {callbackQuery.Data}");
            var dateStr = callbackQuery.Data.Replace("admin_slot_date_", "");
            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var date))
            {
                session.TempSlotDate = date;
                _adminSessionStorage.SaveSession(session);

                // Pass the message ID to edit the same message
                //await ShowAdminTimePicker(chatId, session, callbackQuery.Message.MessageId);

                await ShowTimeSlotsForDay(callbackQuery, date);
            }
            else
            {
                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚫 Ошибка при выборе даты.");
            }

            return;
        }



        if (callbackQuery.Data.StartsWith("admin_time_"))
        {
            var timeStr = callbackQuery.Data.Replace("admin_time_", "").Trim();

            // Now this will correctly parse "09:00", "09:30", etc.
            if (TimeSpan.TryParseExact(timeStr, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
            {
                if (session.TempSlotDate == null)
                {
                    await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Дата не выбрана.");
                    return;
                }

                var newSlot = new TimeSlot
                {
                    Date = session.TempSlotDate.Value.Date,
                    StartTime = time,
                    IsActive = true
                };

                await _repository.AddTimeSlotAsync(newSlot);

                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Окно добавлено!");
                await _adminBotClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId,
                    $"✅ Новое окно добавлено:\n📅 {session.TempSlotDate.Value:dd.MM.yyyy}\n⏰ {time:hh\\:mm}");

                session.TempSlotDate = null;
                _adminSessionStorage.SaveSession(session);
                await ShowTimeSlotsAdminCalendar(callbackQuery, session);
            }
            else
            {
                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚫 Неверное время.");
            }

            return;
        }

        if (callbackQuery.Data.StartsWith("toggle_time_"))
        {
            var timeStr = callbackQuery.Data.Replace("toggle_time_", "");
            if (TimeSpan.TryParseExact(timeStr, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
            {
                if (session.SelectedTimes.Contains(time))
                    session.SelectedTimes.Remove(time);
                else
                    session.SelectedTimes.Add(time);

                _adminSessionStorage.SaveSession(session);

                // Refresh the same message instead of sending a new one
                await ShowAdminTimePicker(callbackQuery.Message.Chat.Id, session, callbackQuery.Message.MessageId);
            }
            return;
        }

        if (callbackQuery.Data == "save_times")
        {
            if (session.TempSlotDate == null || !session.SelectedTimes.Any())
            {
                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "❌ Выберите хотя бы один слот.");
                return;
            }

            foreach (var time in session.SelectedTimes)
            {
                var newSlot = new TimeSlot
                {
                    Date = session.TempSlotDate.Value.Date,
                    StartTime = time,
                    IsActive = true
                };
                await _repository.AddTimeSlotAsync(newSlot);
            }

            await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Все выбранные окна добавлены!");
            session.SelectedTimes.Clear();
            session.TempSlotDate = null;
            _adminSessionStorage.SaveSession(session);

            await ShowTimeSlotsAdminCalendar(callbackQuery, session);
        }



        // ------------------ Edit or delete existing slot ------------------ //

        if (callbackQuery.Data.StartsWith("edit_timeslot_"))
        {
            var slotId = int.Parse(callbackQuery.Data.Replace("edit_timeslot_", ""));
            await ShowTimeSlotOptions(callbackQuery, slotId);
            return;
        }

        if (callbackQuery.Data.StartsWith("delete_timeslot_"))
        {
            var slotId = int.Parse(callbackQuery.Data.Replace("delete_timeslot_", ""));
            await _repository.DeleteTimeSlotAsync(slotId);
            await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Окно удалёно");
            await ShowTimeSlotsAdminCalendar(callbackQuery, session);
        }
    }

    #endregion

    #region Admin Message Handling

    public async Task HandleAdminMessage(Message message, CancellationToken ct)
    {
        
        if (!_admins.Contains(message.From.Id))
        {
            await _adminBotClient.EditMessageTextAsync(message.Chat.Id, message.MessageId,
                "⛔ Доступ запрещён. Этот бот только для админов."
            );
            return;
        }
        if (message?.Text == null) return;

        var chatId = message.Chat.Id;
        var adminId = message.From.Id;
        var session = _adminSessionStorage.GetOrCreateSession(adminId);
        if (message.Text == "/start")
        {
            await _adminBotClient.SendTextMessageAsync(message.Chat.Id,
                "👋 Добро пожаловать в админ-панель!\nВыберите действие ниже:",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💼 Управление услугами", "admin_services") },
                    new[] { InlineKeyboardButton.WithCallbackData("📅 Просмотр всех записей", "admin_bookings") },
                    new[] { InlineKeyboardButton.WithCallbackData("🕒 Управление окнами", "show_timeslots") },
                    new[] { InlineKeyboardButton.WithCallbackData("⚙️ Настройки студии", "admin_settings") }
                }));
            return;
        }

        // ---------------- Step 1: Get service name ----------------
        if (_adminSessionStorage.GetSelectedService(adminId) == -1)
        {
            session.TempServiceName = message.Text;
            _adminSessionStorage.SetSelectedService(adminId, 1); // move to "waiting for price"
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.EditMessageTextAsync(chatId, message.MessageId, "Введите цену для новой услуги (в ₽):");
            return;
        }

        // ---------------- Step 2: Get service price ----------------
        if (_adminSessionStorage.GetSelectedService(adminId) == 1 && !string.IsNullOrEmpty(session.TempServiceName))
        {
            if (int.TryParse(message.Text, out var price))
            {
                session.TempServicePrice = price;
                _adminSessionStorage.SetSelectedService(adminId, 2); // move to "waiting for duration"
                _adminSessionStorage.SaveSession(session);
                await _adminBotClient.EditMessageTextAsync(chatId, message.MessageId, "Введите продолжительность услуги (в минутах):");
            }
            else
            {
                await _adminBotClient.EditMessageTextAsync(chatId, message.MessageId, "🚫 Некорректная цена. Введите число:");
            }

            return;
        }

        // ---------------- Step 3: Get service duration ----------------
        if (_adminSessionStorage.GetSelectedService(adminId) == 2 &&
            !string.IsNullOrEmpty(session.TempServiceName) &&
            session.TempServicePrice.HasValue)
        {
            if (int.TryParse(message.Text, out var duration))
            {
                var service = new Service
                {
                    Name = session.TempServiceName,
                    Price = session.TempServicePrice.Value,
                    DurationMinutes = duration,
                    IsActive = true
                };

                await _repository.AddServiceAsync(service);

                // Clear session
                _adminSessionStorage.SetSelectedService(adminId, 0);
                session.TempServiceName = null;
                session.TempServicePrice = null;
                _adminSessionStorage.SaveSession(session);

                await _adminBotClient.EditMessageTextAsync(chatId, message.MessageId, $"✅ Услуга «{service.Name}» успешно добавлена!");
                await ShowServices(chatId, message.MessageId);
            }
            else
            {
                await _adminBotClient.EditMessageTextAsync(chatId, message.MessageId,
                    "🚫 Некорректная продолжительность. Введите число в минутах:");
            }
        }

        // ---------------- Handle editing existing service ----------------
        if (!string.IsNullOrEmpty(session.ActionType) && session.TempServiceId.HasValue)
        {
            var serviceId = session.TempServiceId.Value;
            var action = session.ActionType;

            if (action == "price")
            {
                if (int.TryParse(message.Text, out var newPrice))
                {
                    await _repository.UpdateServicePriceAsync(serviceId, newPrice);
                    await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Цена обновлена!");
                    await Task.Delay(1000);
                    await _adminBotClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                    await ShowServiceOptions(chatId, session.LastBotMessageId, serviceId);
                    //await ShowServices(chatId, session.LastBotMessageId);
                    _adminSessionStorage.SaveSession(session);
                    return;
                }
                else
                {
                    await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "🚫 Некорректная цена. Введите число:");
                    _adminSessionStorage.SaveSession(session);
                    return;
                }
            }
            else if (action == "duration")
            {
                if (int.TryParse(message.Text, out var newDuration))
                {
                    await _repository.UpdateServiceDurationAsync(serviceId, newDuration);
                    await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Продолжительность обновлена!");
                    await Task.Delay(1000);
                    await _adminBotClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                    await ShowServiceOptions(chatId, session.LastBotMessageId, serviceId);
                    await ShowServices(chatId, session.LastBotMessageId);
                    _adminSessionStorage.SaveSession(session);
                    return;
                }
                else
                {
                    await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId,

                        "🚫 Некорректная продолжительность. Введите число:");
                    _adminSessionStorage.SaveSession(session);
                    return;
                }
            }

            // Reset session after update
            session.TempServiceId = null;
            session.ActionType = null;
            _adminSessionStorage.SaveSession(session);
           

            await ShowServices(chatId,message.MessageId);
        }

        // Handle other cases (editing price/duration etc.) here...
        // ----- Studio Settings updates -----
        switch (session.ActionType)
        {
            case "edit_studio_address":
                {
                    await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                    var studio = await _repository.GetStudioAsync();
                    studio.Address = message.Text;
                    await _repository.UpdateStudioAsync(studio);
                    session.ActionType = null;
                    _adminSessionStorage.SaveSession(session);
                    //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Адрес обновлён!");
                    //await Task.Delay(1500);
                    await ShowStudioSettings(chatId, session.LastBotMessageId);
                    return;
                }

            case "edit_studio_name":
                {
                    await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                    var studio = await _repository.GetStudioAsync();
                    studio.Name = message.Text;
                    await _repository.UpdateStudioAsync(studio);
                    session.ActionType = null;
                    _adminSessionStorage.SaveSession(session);
                    //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Название обновлено!");
                    //await Task.Delay(1500);
                    await ShowStudioSettings(chatId, session.LastBotMessageId);
                    return;
                }

            case "edit_studio_phone":
                {
                    await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                    var studio = await _repository.GetStudioAsync();
                    studio.Phone = message.Text;
                    await _repository.UpdateStudioAsync(studio);
                    session.ActionType = null;
                    _adminSessionStorage.SaveSession(session);
                    //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Телефон обновлён!");
                    //await Task.Delay(1500);
                    await ShowStudioSettings(chatId, session.LastBotMessageId);
                    return;
                }

            case "edit_studio_instagram":
                {
                    await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                    var studio = await _repository.GetStudioAsync();
                    studio.Instagram = message.Text;
                    await _repository.UpdateStudioAsync(studio);
                    session.ActionType = null;
                    _adminSessionStorage.SaveSession(session);
                    //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Instagram обновлён!");
                    await ShowStudioSettings(chatId, session.LastBotMessageId);
                    return;
                }
            case "edit_studio_telegram":
            {
                await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                var studio = await _repository.GetStudioAsync();
                studio.Telegram = message.Text;
                await _repository.UpdateStudioAsync(studio);
                session.ActionType = null;
                _adminSessionStorage.SaveSession(session);
                //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Telegram обновлён!");
                await ShowStudioSettings(chatId, session.LastBotMessageId);
                return;
            }

            case "edit_studio_description":
                {
                    await _adminBotClient.DeleteMessageAsync(chatId, message.MessageId);
                    var studio = await _repository.GetStudioAsync();
                    studio.Description = message.Text;
                    await _repository.UpdateStudioAsync(studio);
                    session.ActionType = null;
                    _adminSessionStorage.SaveSession(session);
                    //await _adminBotClient.EditMessageTextAsync(chatId, session.LastBotMessageId, "✅ Описание обновлено!");
                    await ShowStudioSettings(chatId, session.LastBotMessageId);
                    return;
                }
        }
       
    }

    #endregion

    #region Admin Menus & Services
    private async Task ShowStudioSettings(long chatId, int messageId)
    {
        var studio = await _repository.GetStudioAsync();

        var text =
            "<b>Настройки студии</b>\n\n" +
            $"🏷 Название: {studio.Name}\n" +
            $"📍 Адрес: {studio.Address}\n" +
            $"📞 Телефон: {studio.Phone}\n" +
            $"✈️ Telegram: {studio.Telegram}\n" +
            $"📸 Instagram: {studio.Instagram}\n" +
            $"📝 Описание: {studio.Description}";

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🏷 Изменить название студии", "edit_studio_name") },
            new[] { InlineKeyboardButton.WithCallbackData("📍 Изменить адрес", "edit_studio_address") },
            new[] { InlineKeyboardButton.WithCallbackData("📞 Изменить телефон", "edit_studio_phone") },
            new[] { InlineKeyboardButton.WithCallbackData("✈️ Изменить Telegram", "edit_studio_telegram") },
            new[] { InlineKeyboardButton.WithCallbackData("📸 Изменить Instagram", "edit_studio_instagram") },
            new[] { InlineKeyboardButton.WithCallbackData("📝 Изменить описание", "edit_studio_description") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_main") }
        });

        await _adminBotClient.EditMessageTextAsync(
            chatId: chatId,
            messageId: messageId,
            text: text,
            parseMode: ParseMode.Html,
            replyMarkup: buttons
        );
    }

    private async Task ShowAdminMainMenu(CallbackQuery callbackQuery)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var adminId = callbackQuery.From.Id;
        // Clear any temporary session data
        var session = _adminSessionStorage.GetOrCreateSession(adminId);
        session.TempServiceName = null;
        session.TempServiceId = null;
        session.TempSlotDate = null;
        session.TempSlotTime = null;
        session.TempSlotAction = null;
        session.ActionType = null;
        _adminSessionStorage.SetSelectedService(adminId, 0); // reset multi-step status
        _adminSessionStorage.SaveSession(session);

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💼 Управление услугами", "admin_services") },
            new[] { InlineKeyboardButton.WithCallbackData("📅 Просмотр всех записей", "admin_bookings") },
            new[] { InlineKeyboardButton.WithCallbackData("🕒 Управление окнами", "show_timeslots") },
            new[] { InlineKeyboardButton.WithCallbackData("⚙️ Настройки студии", "admin_settings") }
        });

        await _adminBotClient.EditMessageTextAsync(chatId,callbackQuery.Message.MessageId,
            "<b>Админ панель</b>\nВыберите действие:",
            parseMode: ParseMode.Html,
            replyMarkup: buttons);
    }

    private async Task ShowServices(long chatId, int messageId)
    {
        var services = await _repository.GetAvailableServicesAsync();
        var messageText = "<b>Управление услугами</b>\n\n";
        foreach (var s in services)
            messageText += $"• {s.Name} — {s.Price}₽ — {s.DurationMinutes} мин\n";
        messageText += "\nВыберите услугу для редактирования или добавьте новую:";

        var buttons = services.Select(s =>
            new[] { InlineKeyboardButton.WithCallbackData(s.Name, $"service_{s.Id}") }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить услугу", "admin_add_service") });
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") });

        await _adminBotClient.EditMessageTextAsync(chatId,messageId, messageText,
            parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task ShowServiceOptions(long chatId,int messageId, int serviceId)
    {
        var service = await _repository.GetServiceByIdAsync(serviceId);
        if (service == null) return;

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💰 Изменить цену", $"edit_price_{service.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("⏱ Изменить продолжительность", $"edit_duration_{service.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("❌ Удалить услугу", $"delete_service_{service.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_services") }
        });

        await _adminBotClient.EditMessageTextAsync(chatId,
            messageId,
            $"<b>{service.Name}</b>\nТекущая цена: {service.Price}₽\nПродолжительность: {service.DurationMinutes} мин",
            ParseMode.Html,
            replyMarkup: buttons);
    }

    private async Task ShowAllBookings(CallbackQuery callbackQuery)
    {
        var bookings = await _repository.GetAllBookingsAsync();
        if (!bookings.Any())
        {
            await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id,callbackQuery.Message.MessageId, "Записей пока нет.");
            return;
        }

        var buttons = bookings.Select(b =>
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{b.Date:dd.MM.yyyy} {b.TimeSlot} — {b.User.Username}",
                    $"booking_{b.Id}")
            }).ToList();
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") });

        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId,
            "<b>Все записи</b>\nВыберите запись для управления:",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task ShowBookingDetails(CallbackQuery callbackQuery, int bookingId)
    {
        var booking = await _repository.GetBookingByIdAsync(bookingId);
        if (booking == null) return;

        // Safely get phone and username
        var phone = string.IsNullOrWhiteSpace(booking.User.Phone) ? "Не указан" : booking.User.Phone;
        var username = string.IsNullOrWhiteSpace(booking.User.Username) ? "Не указан" : booking.User.Username;

        // Safely get services
        var services = booking.BookingServices != null && booking.BookingServices.Any()
            ? string.Join(", ", booking.BookingServices.Select(bs => bs.Service?.Name ?? "Не указано"))
            : "Нет услуг";

        var message = $"📅 {booking.Date:dd.MM.yyyy} ⏰ {booking.TimeSlot:hh\\:mm}\n" +
                      $"👤 Клиент: @{username}\n" +
                      $"📞 Телефон: {phone}\n" +
                      $"👩‍🎨 Мастер: {booking.Master?.Name ?? "Не указан"}\n" +
                      $"🧾 Услуги: {services}\n\nВы можете отменить эту запись:";

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("❌ Отменить запись", $"cancel_booking_{booking.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_bookings") },
            new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") }
        });

        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id,
            callbackQuery.Message.MessageId,
            message,
            ParseMode.Html,
            replyMarkup: buttons);
    }

    private async Task CancelBooking(CallbackQuery callbackQuery, int bookingId)
    {
        var booking = await _repository.GetBookingByIdAsync(bookingId);
        if (booking == null) return;

        await _repository.CancelBookingAsync(bookingId);
        await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Запись отменена!");
        try
        {
            await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId,
                $"❌ Запись на {booking.Date:dd.MM.yyyy} {booking.TimeSlot} была отменена.");
        }
        catch
        {
        }

        await ShowAllBookings(callbackQuery);
    }

    #endregion

    #region Time Slot Management

    //private async Task ShowTimeSlots(long chatId)
    //{
    //    var slots = await _repository.GetAllTimeSlotsAsync();
    //    if (!slots.Any())
    //    {
    //        await _adminBotClient.SendTextMessageAsync(chatId, "Окон пока нет.");
    //        return;
    //    }

    //    // Группировка по дате
    //    var grouped = slots
    //        .OrderBy(s => s.Date)
    //        .ThenBy(s => s.StartTime)
    //        .GroupBy(s => s.Date);

    //    foreach (var group in grouped)
    //    {
    //        var date = group.Key.ToString("dd.MM.yyyy");
    //        var lines = $"<b>{date}</b>\n";

    //        var buttons = new List<InlineKeyboardButton[]>();

    //        foreach (var slot in group)
    //        {
    //            var count = await _repository.GetBookingCountForSlotAsync(slot.Date, slot.StartTime);

    //            string label = count == 0
    //                ? $"{slot.StartTime:hh\\:mm} — свободно"
    //                : $"{slot.StartTime:hh\\:mm} — занято ({count})";

    //            buttons.Add(new[]
    //            {
    //                InlineKeyboardButton.WithCallbackData(label, $"edit_timeslot_{slot.Id}")
    //            });
    //        }

    //        await _adminBotClient.SendTextMessageAsync(
    //            chatId,
    //            lines,
    //            parseMode: ParseMode.Html,
    //            replyMarkup: new InlineKeyboardMarkup(buttons)
    //        );
    //    }

    //    // Buttons at the bottom
    //    await _adminBotClient.SendTextMessageAsync(
    //        chatId,
    //        "Дополнительно:",
    //        replyMarkup: new InlineKeyboardMarkup(new[]
    //        {
    //            new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить окно", "add_timeslot") },
    //            new[] { InlineKeyboardButton.WithCallbackData("📅 Оповестить о свободных окнах", "send_all_slots") },
    //            new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") }
    //        })
    //    );
    //}


    private async Task ShowAdminCalendar(CallbackQuery callbackQuery, AdminSession session)
    {
        if (session.CurrentMonth == default)
            session.CurrentMonth = DateTime.Today;
        _adminSessionStorage.SaveSession(session);

        var buttons = await BuildAdminCalendarAsync(session.CurrentMonth);
        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId,
            $"<b>Выберите дату:</b>\n\n{session.CurrentMonth.ToString("MMMM yyyy", _ruCulture)}",
            parseMode: ParseMode.Html,
            replyMarkup: buttons);
    }
    private async Task ShowTimeSlotsAdminCalendar(CallbackQuery callbackQuery, AdminSession session)
    {
        if (session.CurrentMonth == default)
            session.CurrentMonth = DateTime.Today;
        _adminSessionStorage.SaveSession(session);

        var buttons = await BuildAdminCalendarWithBookingsAsync(session.CurrentMonth);
        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId,
            $"<b>Выберите дату:</b>\n\n{session.CurrentMonth.ToString("MMMM yyyy", _ruCulture)}",
            parseMode: ParseMode.Html,
            replyMarkup: buttons);
    }



    private async Task<InlineKeyboardMarkup> BuildAdminCalendarAsync(DateTime month)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        // Month navigation
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("⬅️", "prev_month"),
        InlineKeyboardButton.WithCallbackData($"{month.ToString("MMMM yyyy", _ruCulture)}", "ignore"),
        InlineKeyboardButton.WithCallbackData("➡️", "next_month")
    });

        // Weekday headers
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("Пн", "ignore"),
        InlineKeyboardButton.WithCallbackData("Вт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Ср", "ignore"),
        InlineKeyboardButton.WithCallbackData("Чт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Пт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Сб", "ignore"),
        InlineKeyboardButton.WithCallbackData("Вс", "ignore")
    });

        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var firstDay = ((int)new DateTime(month.Year, month.Month, 1).DayOfWeek + 6) % 7 + 1;
        var dayCounter = 1;

        var allSlots = await _repository.GetAllTimeSlotsAsync();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTime.Now, tz);

        for (var week = 0; week < 6; week++)
        {
            var row = new List<InlineKeyboardButton>();
            for (var dow = 1; dow <= 7; dow++)
            {
                if ((week == 0 && dow < firstDay) || dayCounter > daysInMonth)
                {
                    row.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));
                }
                else
                {
                    var date = new DateTime(month.Year, month.Month, dayCounter);

                    // Past day → mark as 🚫
                    if (date < DateTime.Today)
                    {
                        row.Add(InlineKeyboardButton.WithCallbackData("🚫", "ignore"));
                        dayCounter++;
                        continue;
                    }

                    var daySlots = allSlots
                        .Where(ts => ts.Date.Date == date.Date && ts.IsActive)
                        .Select(ts => ts.StartTime)
                        .ToList();

                    bool allOccupied = true;
                    var start = new TimeSpan(9, 0, 0);
                    var end = new TimeSpan(20, 0, 0);

                    for (var t = start; t <= end; t = t.Add(TimeSpan.FromMinutes(30)))
                    {
                        var slotDateTime = date + t;

                        // Skip times in the past (today) or already occupied
                        if (slotDateTime > now && !daySlots.Contains(t))
                        {
                            allOccupied = false;
                            break;
                        }
                    }

                    if (allOccupied)
                        row.Add(InlineKeyboardButton.WithCallbackData($"🚫", "ignore"));
                    else
                        row.Add(InlineKeyboardButton.WithCallbackData(date.Day.ToString(),
                            $"admin_date_{date:yyyy-MM-dd}"));

                    dayCounter++;
                }
            }

            buttons.Add(row.ToArray());
        }

        return new InlineKeyboardMarkup(buttons);
    }


    private async Task ShowAdminTimePicker(long chatId, AdminSession session, int messageId)
    {
        if (session.TempSlotDate == null)
            return;

        var allSlots = await _repository.GetAllTimeSlotsAsync();

        // Get only occupied times (already added slots)
        var occupiedTimes = allSlots
            .Where(ts => ts.Date.Date == session.TempSlotDate.Value.Date && ts.IsActive)
            .Select(ts => ts.StartTime)
            .ToHashSet();

        var possibleTimes = new List<TimeSpan>();
        var start = new TimeSpan(9, 0, 0);
        var end = new TimeSpan(20, 0, 0);

        var tz = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTime.Now, tz);

        for (var t = start; t <= end; t = t.Add(TimeSpan.FromMinutes(30)))
        {
            var slotDateTime = session.TempSlotDate.Value.Date + t;
            if (slotDateTime <= now || occupiedTimes.Contains(t))
                continue;
            possibleTimes.Add(t);
        }

        if (!possibleTimes.Any())
        {
            await _adminBotClient.EditMessageTextAsync(
                chatId,
                messageId,
                $"🚫 Все окошки заняты или прошли для {session.TempSlotDate:dd.MM.yyyy}. Выберите другую дату.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
            );
            return;
        }

        // Build buttons
        var buttons = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < possibleTimes.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>();
            for (var j = i; j < i + 2 && j < possibleTimes.Count; j++)
            {
                var time = possibleTimes[j];
                var isSelected = session.SelectedTimes.Contains(time);
                var timeStr = time.ToString(@"hh\:mm");
                var buttonText = isSelected ? $"✅ {timeStr}" : timeStr;

                row.Add(InlineKeyboardButton.WithCallbackData(buttonText, $"toggle_time_{timeStr}"));
            }
            buttons.Add(row.ToArray());
        }

        // Save & Back buttons
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("💾 Сохранить", "save_times"),
        InlineKeyboardButton.WithCallbackData("⬅️ Назад", "show_timeslots")
    });

        // Edit the existing message instead of sending a new one
        await _adminBotClient.EditMessageTextAsync(
            chatId,
            messageId,
            $"<b>Выберите свободные окошки для {session.TempSlotDate:dd.MM.yyyy}</b>\n(выбранные отмечены ✅)",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons)
        );
    }

    private async Task ShowTimeSlotOptions(CallbackQuery callbackQuery, int slotId)
    {
        var slot = await _repository.GetTimeSlotByIdAsync(slotId);
        if (slot == null) return;

        var chatId = callbackQuery.Message.Chat.Id;

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("❌ Удалить окно", $"delete_timeslot_{slot.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "show_timeslots") }
        });

        var count = await _repository.GetBookingCountForSlotAsync(slot.Date, slot.StartTime);

        var status = count == 0
            ? "Свободно"
            : $"Занято ({count})";

        await _adminBotClient.EditMessageTextAsync(
            chatId,
            callbackQuery.Message.MessageId,
            $"Окно: <b>{slot.StartTime:hh\\:mm}</b>\n" +
            $"Дата: {slot.Date:dd.MM.yyyy}\n" +
            $"Статус: {status}",
            ParseMode.Html,
            replyMarkup: buttons
        );
    }




    private async Task<InlineKeyboardMarkup> BuildAdminCalendarWithBookingsAsync(DateTime month)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        // Month navigation
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("⬅️", "prev_month"),
        InlineKeyboardButton.WithCallbackData($"{month.ToString("MMMM yyyy", _ruCulture)}", "ignore"),
        InlineKeyboardButton.WithCallbackData("➡️", "next_month")
    });

        // Weekday headers
        buttons.Add(new[]
        {
        InlineKeyboardButton.WithCallbackData("Пн", "ignore"),
        InlineKeyboardButton.WithCallbackData("Вт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Ср", "ignore"),
        InlineKeyboardButton.WithCallbackData("Чт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Пт", "ignore"),
        InlineKeyboardButton.WithCallbackData("Сб", "ignore"),
        InlineKeyboardButton.WithCallbackData("Вс", "ignore")
    });

        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        int firstDay = ((int)new DateTime(month.Year, month.Month, 1).DayOfWeek + 6) % 7 + 1;
        int dayCounter = 1;

        var allSlots = await _repository.GetAllTimeSlotsAsync();

        for (int week = 0; week < 6; week++)
        {
            var row = new List<InlineKeyboardButton>();
            for (int dow = 1; dow <= 7; dow++)
            {
                if ((week == 0 && dow < firstDay) || dayCounter > daysInMonth)
                {
                    row.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));
                }
                else
                {
                    var date = new DateTime(month.Year, month.Month, dayCounter);

                    // Past dates → disable
                    if (date < DateTime.Today)
                    {
                        row.Add(InlineKeyboardButton.WithCallbackData("🚫", "ignore"));
                        dayCounter++;
                        continue;
                    }

                    // Check if there are any bookings for this date
                    var slotsForDate = allSlots
                        .Where(s => s.Date.Date == date.Date && s.IsActive)
                        .ToList();

                    bool hasBooking = false;
                    foreach (var slot in slotsForDate)
                    {
                        var count = await _repository.GetBookingCountForSlotAsync(slot.Date, slot.StartTime);
                        if (count > 0)
                        {
                            hasBooking = true;
                            break;
                        }
                    }

                    string label = hasBooking ? $"✅ {date.Day}" : date.Day.ToString();
                    row.Add(InlineKeyboardButton.WithCallbackData(label, $"admin_slot_date_{date:yyyy-MM-dd}"));

                    dayCounter++;
                }
            }

            buttons.Add(row.ToArray());

           

        }
        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("📅 Оповестить о свободных окнах", "send_all_slots"),
        });
        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main")
        });
        
        return new InlineKeyboardMarkup(buttons);
    }


    private async Task ShowTimeSlotsForDay(CallbackQuery callbackQuery, DateTime date)
    {
        var slots = await _repository.GetAllTimeSlotsAsync();
        var daySlots = slots.Where(s => s.Date.Date == date.Date).OrderBy(s => s.StartTime).ToList();

        //if (!daySlots.Any())
        //{
        //    await _adminBotClient.SendTextMessageAsync(chatId, "Окон на этот день нет.");
        //    return;
        //}

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var slot in daySlots)
        {
            var count = await _repository.GetBookingCountForSlotAsync(slot.Date, slot.StartTime);
            string label = count == 0 ? $"{slot.StartTime:hh\\:mm} — свободно" : $"{slot.StartTime:hh\\:mm} — занято ({count})";
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(label, $"edit_timeslot_{slot.Id}") });
        }
        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("➕ Добавить окно", "add_timeslot") ,

        });

        buttons.Add(new[]
        {

            InlineKeyboardButton.WithCallbackData("⬅️ Назад", "show_timeslots")
        });

        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id,callbackQuery.Message.MessageId, 
            $"<b>Окошки на {date:dd.MM.yyyy}</b>",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons));
    }




    #endregion
}