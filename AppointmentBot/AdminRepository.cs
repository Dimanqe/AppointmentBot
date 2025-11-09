#region

using AppointmentBot.Data;
using AppointmentBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Telegram.Bot;

#endregion

namespace AppointmentBot;

public class AdminRepository : BotRepository
{
    public AdminRepository(BotDbContext context, AdminBotClient adminBot, UserBotClient userBotClient)
        : base(context, adminBot, userBotClient)
    {
    }

    // --- Service management ---
    public async Task<Service> AddServiceAsync(Service service)
    {
        _context.Services.Add(service);
        await _context.SaveChangesAsync();
        return service;
    }

    public async Task<bool> UpdateServicePriceAsync(int serviceId, int newPrice)
    {
        var service = await _context.Services.FindAsync(serviceId);
        if (service == null) return false;

        service.Price = newPrice;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<Service?> GetServiceByIdAsync(int serviceId)
    {
        return await _context.Services.FindAsync(serviceId);
    }

    public async Task<bool> DeleteServiceAsync(int serviceId)
    {
        var service = await _context.Services.FindAsync(serviceId);
        if (service == null) return false;

        _context.Services.Remove(service);
        await _context.SaveChangesAsync();
        return true;
    }

    // --- Admin-only: get all bookings ---
    public async Task<List<Booking>> GetAllBookingsAsync(DateTime? from = null, DateTime? to = null)
    {
        var query = _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Master)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .AsQueryable();

        //if (from.HasValue)
        //    query = query.Where(b => b.Date >= from.Value);

        //if (to.HasValue)
        //    query = query.Where(b => b.Date <= to.Value);

        return await query.OrderBy(b => b.Date).ToListAsync();
    }

    public async Task<bool> UpdateServiceDurationAsync(int serviceId, int durationMinutes)
    {
        var service = await _context.Services.FindAsync(serviceId);
        if (service == null) return false;

        service.DurationMinutes = durationMinutes;
        await _context.SaveChangesAsync();
        return true;
    }

    // --- TimeSlot management for admin ---
    public async Task<List<TimeSlot>> GetAllTimeSlotsAsync()
    {
        return await _context.TimeSlots
            .OrderBy(ts => ts.Date)
            .ThenBy(ts => ts.StartTime)
            .ToListAsync();
    }

    public async Task<TimeSlot?> GetTimeSlotByIdAsync(int id)
    {
        return await _context.TimeSlots.FindAsync(id);
    }

    public async Task AddTimeSlotAsync(TimeSlot slot)
    {
        _context.TimeSlots.Add(slot);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateTimeSlotAsync(TimeSlot slot)
    {
        _context.TimeSlots.Update(slot);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteTimeSlotAsync(int id)
    {
        var slot = await _context.TimeSlots.FindAsync(id);
        if (slot != null)
        {
            _context.TimeSlots.Remove(slot);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<TimeSpan>> GetAllPossibleTimesAsync()
    {
        // Example: every 30 minutes from 09:00 to 20:00
        var times = new List<TimeSpan>();
        var start = new TimeSpan(9, 0, 0);
        var end = new TimeSpan(20, 0, 0);

        for (var t = start; t <= end; t = t.Add(TimeSpan.FromMinutes(30)))
            times.Add(t);

        return await Task.FromResult(times);
    }

    public new async Task<bool> CancelBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking == null)
            return false;

        // Free up the corresponding time slot
        var slot = await _context.TimeSlots
            .FirstOrDefaultAsync(ts => ts.Date == booking.Date && ts.StartTime == booking.TimeSlot);

        if (slot != null)
        {
            slot.IsOccupied = false;
            _context.TimeSlots.Update(slot);
        }

        // Remove booking (cascades BookingServices)
        _context.Bookings.Remove(booking);
        await _context.SaveChangesAsync();

        // Prepare message for the user
        var username = string.IsNullOrWhiteSpace(booking.User.Username) ? "Не указан" : booking.User.Username;
        var servicesText = booking.BookingServices.Any()
            ? string.Join(", ", booking.BookingServices.Select(bs => bs.Service?.Name ?? "Не указано"))
            : "Нет услуг";

        var message = "\u274c Ваша запись была отменена!\n" +
                      $"💇 Услуга: {servicesText}\n" +
                      $"📅 Дата: {booking.Date:dd.MM.yyyy}\n" +
                      $"⏰ Время: {booking.TimeSlot:hh\\:mm}";

        try
        {
            await _userBotClient.Client.SendTextMessageAsync(
                booking.UserId,
                message
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки уведомления пользователю: {ex.Message}");
        }

        return true;
    }
    public async Task SendAllFreeSlotsAsync(long adminChatId)
    {
        // Получаем все свободные и активные слоты (не занятые)
        var freeSlots = await _context.TimeSlots
            .Where(s => s.Date >= DateTime.Today && s.IsActive && !s.IsOccupied)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ToListAsync();

      

        // Формируем сообщение
        var sb = new StringBuilder();
        sb.AppendLine("📅 Свободные окошки:");
        sb.AppendLine();

        foreach (var s in freeSlots)
            sb.AppendLine($"• {s.Date:dd.MM.yyyy} — {s.StartTime:hh\\:mm}");

        sb.AppendLine();
        sb.AppendLine("Запишитесь прямо сейчас в боте 💬");

        var message = sb.ToString();

        // Получаем всех пользователей
        var users = await _context.Users.ToListAsync();

        //foreach (var user in users)
        //{
        //    try
        //    {
        //        await _userBotClient.Client.SendTextMessageAsync(
        //            chatId: user.Id,
        //            text: message,
        //            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
        //        );
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Ошибка отправки пользователю {user.Id}: {ex.Message}");
        //    }
        //}
        if (freeSlots.Any())
        {
            await _adminBot.Client.SendTextMessageAsync(
                chatId: _adminBot.NotificationChannel,
                text: message
            );
        }

        await _adminBot.Client.SendTextMessageAsync(
            adminChatId,
            "✅ Все свободные окна отправлены пользователям."
        );
    }



}