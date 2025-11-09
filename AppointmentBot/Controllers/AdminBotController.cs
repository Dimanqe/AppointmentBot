#region

using System.Globalization;
using AppointmentBot.Models;
using AppointmentBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

#endregion

namespace AppointmentBot.Controllers;

public class AdminBotController
{
    private readonly ITelegramBotClient _adminBotClient;
    private readonly IAdminSessionStorage _adminSessionStorage;
    private readonly AdminRepository _repository;
    private readonly CultureInfo _ruCulture = new("ru-RU");

    public AdminBotController(AdminBotClient adminBotClient,
        AdminRepository repository,
        IAdminSessionStorage adminSessionStorage)
    {
        _adminBotClient = adminBotClient.Client;
        _repository = repository;
        _adminSessionStorage = adminSessionStorage;
    }

    #region CallbackQuery Handling

    public async Task Handle(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery?.Data == null || callbackQuery.Message == null) return;

        var chatId = callbackQuery.Message.Chat.Id;
        var adminId = callbackQuery.From.Id;
        var session = _adminSessionStorage.GetOrCreateSession(adminId);

        switch (callbackQuery.Data)
        {
            case "admin_main":
                await ShowAdminMainMenu(callbackQuery);
                return;
            case "admin_services":
                await ShowServices(chatId);
                return;
            case "admin_add_service":
                _adminSessionStorage.SetSelectedService(adminId, -1);
                await _adminBotClient.SendTextMessageAsync(chatId, "Введите имя новой услуги:");
                return;
            case "admin_bookings":
                await ShowAllBookings(chatId);
                return;
            case "show_timeslots":
                await ShowTimeSlots(chatId);
                return;
            case "prev_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(-1);
                _adminSessionStorage.SaveSession(session);
                await ShowAdminCalendar(chatId, session);
                return;
            case "next_month":
                session.CurrentMonth = session.CurrentMonth.AddMonths(1);
                _adminSessionStorage.SaveSession(session);
                await ShowAdminCalendar(chatId, session);
                return;
        }

        // ------------------ Existing service handling ------------------ //

        if (callbackQuery.Data.StartsWith("service_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("service_", ""));
            await ShowServiceOptions(callbackQuery, serviceId);
            return;
        }

        if (callbackQuery.Data.StartsWith("edit_price_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("edit_price_", ""));
            session.TempServiceId = serviceId;
            session.ActionType = "price";
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.SendTextMessageAsync(chatId, "Введите новую цену услуги:");
            return;
        }

        if (callbackQuery.Data.StartsWith("edit_duration_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("edit_duration_", ""));
            session.TempServiceId = serviceId;
            session.ActionType = "duration";
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.SendTextMessageAsync(chatId, "Введите новую продолжительность услуги (в минутах):");
            return;
        }

        if (callbackQuery.Data.StartsWith("delete_service_"))
        {
            var serviceId = int.Parse(callbackQuery.Data.Replace("delete_service_", ""));
            await _repository.DeleteServiceAsync(serviceId);
            await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "✅ Услуга удалена");
            await ShowServices(chatId);
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
            await ShowAdminCalendar(chatId, session);
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
                await ShowAdminTimePicker(chatId, session);
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
                await _adminBotClient.SendTextMessageAsync(chatId,
                    $"✅ Новое окно добавлено:\n📅 {session.TempSlotDate.Value:dd.MM.yyyy}\n⏰ {time:hh\\:mm}");

                session.TempSlotDate = null;
                _adminSessionStorage.SaveSession(session);
                await ShowTimeSlots(chatId);
            }
            else
            {
                await _adminBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "🚫 Неверное время.");
            }

            return;
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
            await ShowTimeSlots(chatId);
        }
    }

    #endregion

    #region Admin Message Handling

    public async Task HandleAdminMessage(Message message, CancellationToken ct)
    {
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
                    new[] { InlineKeyboardButton.WithCallbackData("📅 Все записи", "admin_bookings") },
                    new[] { InlineKeyboardButton.WithCallbackData("🕒 Управление окнами", "show_timeslots") }
                }));
            return;
        }

        // ---------------- Step 1: Get service name ----------------
        if (_adminSessionStorage.GetSelectedService(adminId) == -1)
        {
            session.TempServiceName = message.Text;
            _adminSessionStorage.SetSelectedService(adminId, 1); // move to "waiting for price"
            _adminSessionStorage.SaveSession(session);
            await _adminBotClient.SendTextMessageAsync(chatId, "Введите цену для новой услуги (в ₽):");
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
                await _adminBotClient.SendTextMessageAsync(chatId, "Введите продолжительность услуги (в минутах):");
            }
            else
            {
                await _adminBotClient.SendTextMessageAsync(chatId, "🚫 Некорректная цена. Введите число:");
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

                await _adminBotClient.SendTextMessageAsync(chatId, $"✅ Услуга «{service.Name}» успешно добавлена!");
                await ShowServices(chatId);
            }
            else
            {
                await _adminBotClient.SendTextMessageAsync(chatId,
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
                    await _adminBotClient.SendTextMessageAsync(chatId, "✅ Цена обновлена!");
                }
                else
                {
                    await _adminBotClient.SendTextMessageAsync(chatId, "🚫 Некорректная цена. Введите число:");
                    return;
                }
            }
            else if (action == "duration")
            {
                if (int.TryParse(message.Text, out var newDuration))
                {
                    await _repository.UpdateServiceDurationAsync(serviceId, newDuration);
                    await _adminBotClient.SendTextMessageAsync(chatId, "✅ Продолжительность обновлена!");
                }
                else
                {
                    await _adminBotClient.SendTextMessageAsync(chatId,
                        "🚫 Некорректная продолжительность. Введите число:");
                    return;
                }
            }

            // Reset session after update
            session.TempServiceId = null;
            session.ActionType = null;
            _adminSessionStorage.SaveSession(session);

            await ShowServices(chatId);
        }

        // Handle other cases (editing price/duration etc.) here...
    }

    #endregion

    #region Admin Menus & Services

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
            new[] { InlineKeyboardButton.WithCallbackData("🕒 Управление окнами", "show_timeslots") }
        });

        await _adminBotClient.SendTextMessageAsync(chatId,
            "<b>Админ панель</b>\nВыберите действие:",
            parseMode: ParseMode.Html,
            replyMarkup: buttons);
    }

    private async Task ShowServices(long chatId)
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

        await _adminBotClient.SendTextMessageAsync(chatId, messageText,
            parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task ShowServiceOptions(CallbackQuery callbackQuery, int serviceId)
    {
        var service = await _repository.GetServiceByIdAsync(serviceId);
        if (service == null) return;

        var buttons = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("💰 Изменить цену", $"edit_price_{service.Id}") },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⏱ Изменить продолжительность", $"edit_duration_{service.Id}")
            },
            new[] { InlineKeyboardButton.WithCallbackData("❌ Удалить услугу", $"delete_service_{service.Id}") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_services") }
        });

        await _adminBotClient.EditMessageTextAsync(callbackQuery.Message.Chat.Id,
            callbackQuery.Message.MessageId,
            $"<b>{service.Name}</b>\nТекущая цена: {service.Price}₽\nПродолжительность: {service.DurationMinutes} мин",
            ParseMode.Html,
            replyMarkup: buttons);
    }

    private async Task ShowAllBookings(long chatId)
    {
        var bookings = await _repository.GetAllBookingsAsync();
        if (!bookings.Any())
        {
            await _adminBotClient.SendTextMessageAsync(chatId, "Записей пока нет.");
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

        await _adminBotClient.SendTextMessageAsync(chatId,
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
            await _adminBotClient.SendTextMessageAsync(booking.User.Id,
                $"❌ Запись на {booking.Date:dd.MM.yyyy} {booking.TimeSlot} была отменена.");
        }
        catch
        {
        }

        await ShowAllBookings(callbackQuery.Message.Chat.Id);
    }

    #endregion

    #region Time Slot Management

    private async Task ShowTimeSlots(long chatId)
    {
        var slots = await _repository.GetAllTimeSlotsAsync();
        var buttons = slots.Select(ts =>
        {
            return new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{ts.Date:dd.MM.yyyy} {ts.StartTime:hh\\:mm} {(ts.IsActive ? "✅" : "❌")}",
                    $"edit_timeslot_{ts.Id}")
            };
        }).ToList();

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить окно", "add_timeslot") });
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", "admin_main") });

        await _adminBotClient.SendTextMessageAsync(chatId, "<b>Управление окнами</b>",
            parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private async Task ShowAdminCalendar(long chatId, AdminSession session)
    {
        if (session.CurrentMonth == default)
            session.CurrentMonth = DateTime.Today;
        _adminSessionStorage.SaveSession(session);

        var buttons = await BuildAdminCalendarAsync(session.CurrentMonth);
        await _adminBotClient.SendTextMessageAsync(chatId,
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
        var now = DateTime.Now;

        for (var week = 0; week < 6; week++)
        {
            var row = new List<InlineKeyboardButton>();
            for (var dow = 1; dow <= 7; dow++)
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
                        row.Add(InlineKeyboardButton.WithCallbackData($"{date.Day} 🚫", "ignore"));
                    }
                    else
                    {
                        var daySlots = allSlots
                            .Where(ts => ts.Date.Date == date.Date && ts.IsActive)
                            .Select(ts => ts.StartTime)
                            .ToList();

                        var allOccupied = true;

                        var start = new TimeSpan(9, 0, 0);
                        var end = new TimeSpan(20, 0, 0);

                        for (var t = start; t <= end; t = t.Add(TimeSpan.FromMinutes(180)))
                        {
                            var slotDateTime = date + t;
                            if (slotDateTime > now && !daySlots.Contains(t))
                            {
                                allOccupied = false;
                                break;
                            }
                        }

                        if (allOccupied)
                            row.Add(InlineKeyboardButton.WithCallbackData($"{date.Day} 🚫", "ignore"));
                        else
                            row.Add(InlineKeyboardButton.WithCallbackData(date.Day.ToString(),
                                $"admin_date_{date:yyyy-MM-dd}"));
                    }

                    dayCounter++;
                }

            buttons.Add(row.ToArray());
        }

        return new InlineKeyboardMarkup(buttons);
    }

    private async Task ShowAdminTimePicker(long chatId, AdminSession session)
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

        var now = DateTime.Now;

        for (var t = start; t <= end; t = t.Add(TimeSpan.FromMinutes(30)))
        {
            var slotDateTime = session.TempSlotDate.Value.Date + t;

            // Skip if time is in the past or already occupied
            if (slotDateTime <= now || occupiedTimes.Contains(t))
                continue;

            possibleTimes.Add(t);
        }

        if (!possibleTimes.Any())
        {
            await _adminBotClient.SendTextMessageAsync(chatId,
                $"🚫 Все слоты заняты или прошли для {session.TempSlotDate:dd.MM.yyyy}. Выберите другую дату.",
                parseMode: ParseMode.Html);
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < possibleTimes.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>();
            for (var j = i; j < i + 2 && j < possibleTimes.Count; j++)
            {
                var timeStr = possibleTimes[j].ToString(@"hh\:mm");
                row.Add(InlineKeyboardButton.WithCallbackData(timeStr, $"admin_time_{timeStr}"));
            }

            buttons.Add(row.ToArray());
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "show_timeslots") });

        await _adminBotClient.SendTextMessageAsync(
            chatId,
            $"<b>Выберите свободное время для {session.TempSlotDate:dd.MM.yyyy}</b>",
            parseMode: ParseMode.Html,
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

        await _adminBotClient.EditMessageTextAsync(
            chatId,
            callbackQuery.Message.MessageId,
            $"Окно: {slot.StartTime:hh\\:mm} {(slot.IsActive ? "✅ Активно" : "❌ Неактивно")}",
            ParseMode.Html,
            replyMarkup: buttons
        );
    }

    #endregion
}