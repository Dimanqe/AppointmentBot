using AppointmentBot.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

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
                await ProcessRemindersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReminderService");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task ProcessRemindersAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var userBot = scope.ServiceProvider.GetRequiredService<UserBotClient>();

        var now = DateTime.Now;
        var from = now.AddHours(24).AddMinutes(-15);  // window start
        var to = now.AddHours(24).AddMinutes(+15);    // window end

        // Find bookings that need reminder
        var bookings = await db.Bookings
            .Include(b => b.User)
            .Where(b => !b.ReminderSent)
            .ToListAsync();

        foreach (var booking in bookings)
        {
            var bookingDateTime = booking.Date.Date + booking.TimeSlot;

            if (bookingDateTime >= from && bookingDateTime <= to)
            {
                // Send reminder
                await userBot.Client.SendTextMessageAsync(
                    chatId: booking.UserId,
                    text:
                    $"⏰ *Напоминание о записи!* 🧡\n\n" +
                    $"📅 Дата: *{booking.Date:dd.MM.yyyy}*\n" +
                    $"🕒 Время: *{booking.TimeSlot:hh\\:mm}*\n\n" +
                    $"Ждём вас! 😊",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
                );

                booking.ReminderSent = true;
            }
        }

        await db.SaveChangesAsync();
    }
}
