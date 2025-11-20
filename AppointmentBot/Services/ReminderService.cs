using AppointmentBot.Clients;
using AppointmentBot.Data;
using AppointmentBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AppointmentBot.Services;

public class ReminderService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(IServiceProvider services, ILogger<ReminderService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
                await ProcessAutoCancelAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReminderService");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    // =============================
    //      AUTO CANCEL BLOCK
    // =============================

    private async Task ProcessAutoCancelAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var userBot = scope.ServiceProvider.GetRequiredService<UserBotClient>();

        var cancelAfter = DateTime.Now.AddHours(-3);

        var bookingsToCancel = await db.Bookings
            .Include(b => b.User)
            .Where(b => b.ReminderSent)
            .Where(b => !b.ReminderConfirmed)
            .Where(b => b.ReminderSentAt < cancelAfter)
            .ToListAsync(ct);

        foreach (var booking in bookingsToCancel)
        {
            db.Bookings.Remove(booking);

            await userBot.Client.SendTextMessageAsync(
                booking.UserId,
                "⚠️ Вы не подтвердили запись в течение 3 часов, она была автоматически отменена.",
                cancellationToken: ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // =============================
    //        SEND REMINDERS
    // =============================

    private async Task ProcessRemindersAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var userBot = scope.ServiceProvider.GetRequiredService<UserBotClient>();

        var now = DateTime.Now;
        var from = now.AddHours(24).AddMinutes(-40);
        var to = now.AddHours(24).AddMinutes(+40);

        var bookings = await db.Bookings
            .Include(b => b.User)
            .Include(b => b.BookingServices)
                .ThenInclude(bs => bs.Service)
            .Where(b => !b.ReminderSent)
            .ToListAsync(ct);

        foreach (var booking in bookings)
        {
            var bookingTime = booking.Date + booking.TimeSlot;

            if (bookingTime < from || bookingTime > to)
                continue;

            // Сообщение — формируется здесь
            var text = await FormatReminderMessageAsync(booking);

            var buttons = new InlineKeyboardMarkup(new[]
            {
                new [] { InlineKeyboardButton.WithCallbackData("👍 Подтвердить", $"confirm_reminder{booking.Id}") },
                new [] { InlineKeyboardButton.WithCallbackData("❌ Отменить", $"cancel_reminder{booking.Id}") }
            });

            await userBot.Client.SendTextMessageAsync(
                booking.UserId,
                text + "\n\n<b>Подтвердите запись</b>",
                parseMode: ParseMode.Html,
                replyMarkup: buttons,
                cancellationToken: ct);

            booking.ReminderSent = true;
            booking.ReminderSentAt = now;

            db.Bookings.Update(booking);
        }

        await db.SaveChangesAsync(ct);
    }

    // =============================
    //   FORMAT REMINDER MESSAGE
    // =============================

    private async Task<string> FormatReminderMessageAsync(Booking booking)
    {
        var (totalDuration, totalCost) = await CalculateBookingSummaryAsync(booking);

        var services = booking.BookingServices.Any()
            ? Environment.NewLine + string.Join("\n",
                booking.BookingServices.Select(bs => bs.Service!.Name))
            : "не выбрано";

        var date = booking.Date.ToString("dd.MM.yyyy");
        var time = booking.TimeSlot.ToString(@"hh\:mm");

        return
            "⏰ *Напоминание о записи!* 🧡\n" +
            "📍 Студия: A.lash\n" +
            "👩‍🎨 Мастер: Арина\n" +
            "🏠 Адрес: онлайн\n\n" +
            $"⏱️ Продолжительность: {totalDuration.Hours} ч. {totalDuration.Minutes} м.\n" +
            $"💰 Стоимость: {totalCost}₽\n\n" +
            $"🧾 Услуги: {services}\n" +
            $"📅 Дата: {date}\n" +
            $"⏰ Время: {time}\n" +
            "Ждём вас! 😊";
    }

    // =============================
    //   SUMMARIZE SERVICES
    // =============================

    private async Task<(TimeSpan totalDuration, int totalCost)> CalculateBookingSummaryAsync(Booking booking)
    {
        return (
            TimeSpan.FromMinutes(
                booking.BookingServices.Sum(bs => bs.Service!.DurationMinutes)
            ),
            booking.BookingServices.Sum(bs => bs.Service!.Price)
        );
    }
}
