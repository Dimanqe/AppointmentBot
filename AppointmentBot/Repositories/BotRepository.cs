#region

using AppointmentBot.Clients;
using AppointmentBot.Data;
using AppointmentBot.Models;
using AppointmentBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using User = Telegram.Bot.Types.User;

#endregion

namespace AppointmentBot.Repositories;

public class BotRepository
{
    protected readonly AdminBotClient _adminBot;
    protected readonly BotDbContext _context;
    protected readonly UserBotClient _userBotClient;
    private readonly AdminRepository adminRepo;


    public BotRepository(BotDbContext context, AdminBotClient adminBot, UserBotClient userBotClient)
    {
        _context = context;
        _adminBot = adminBot;
        _userBotClient = userBotClient;
        adminRepo = new AdminRepository(_context, _adminBot, _userBotClient);

    }

    // ✅ Create booking with normalized structure
    public async Task<Booking> AddBookingAsync(long userId, int masterId, List<int> serviceIds, DateTime date,
        TimeSpan time, User telegramUser)
    {
        // 1. Get the TimeSlot entity
        var slot = await _context.TimeSlots.FirstOrDefaultAsync(ts => ts.Date == date && ts.StartTime == time);

        if (slot == null) throw new InvalidOperationException("Не удалось найти выбранный временной интервал.");

        if (slot.IsOccupied)
            // Graceful handling — don't throw, just skip
            return null; // or handle it by returning null / error message

        // 2. Mark slot as occupied
        slot.IsOccupied = true;
        _context.TimeSlots.Update(slot);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            user = new Models.User
            {
                Id = userId,
                CreatedAt = DateTime.Now,
                Username = string.IsNullOrWhiteSpace(telegramUser.Username)
                    ? $"{telegramUser.FirstName} {telegramUser.LastName}".Trim()
                    : telegramUser.Username,
                Phone = "" // you can set this later if you collect it
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }

        var booking = new Booking
        {
            UserId = userId,
            MasterId = masterId,
            Date = date,
            TimeSlot = time,
            CreatedAt = DateTime.Now,
            ReminderSent = false
        };

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        // Link services
        var bookingServices = serviceIds.Select(sid => new BookingService
        {
            BookingId = booking.Id,
            ServiceId = sid
        }).ToList();

        _context.BookingServices.AddRange(bookingServices);
        await _context.SaveChangesAsync();

        var username = string.IsNullOrWhiteSpace(user.Username) ? "Не указан" : user.Username;
        var phone = string.IsNullOrWhiteSpace(user.Phone) ? "Не указан" : user.Phone;

        var servicesText = bookingServices.Any()
            ? string.Join(", ",
                bookingServices.Select(bs => _context.Services.Find(bs.ServiceId)?.Name ?? "Не указано"))
            : "Нет услуг";

        var message = "\ud83d\udcc5 Новое бронирование!\n" +
                      $"👤 Клиент: @{username}\n" +
                      $"📞 Телефон: {phone}\n" +
                      $"💇 Услуга: {servicesText}\n" +
                      $"📅 Дата: {booking.Date:dd.MM.yyyy}\n" +
                      $"⏰ Время: {booking.TimeSlot:hh\\:mm}";

        try
        {
           
            foreach (var adminId in _adminBot.AdminChatIds)
            {
                await _adminBot.Client.SendTextMessageAsync(
                    adminId,
                    message
                );
            }

            foreach (var adminId in _adminBot.AdminChatIdsForChannelMessageUpdate)
            {
                await adminRepo.SendAllFreeSlotsAsync(adminId);
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки уведомления админ-боту: {ex.Message}");
        }

        return booking;
    }

    // ✅ Retrieve all bookings for a specific user
    public async Task<List<Booking>> GetUserBookingsAsync(long userId)
    {
        return await _context.Bookings
            .Include(b => b.Master)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.Date)
            .ToListAsync();
    }

    // ✅ Retrieve available services
    public async Task<List<Service>> GetAvailableServicesAsync()
    {
        return await _context.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .ToListAsync();
    }

    // ✅ Retrieve available masters (future-proof)
    public async Task<List<Master>> GetMastersAsync()
    {
        return await _context.Masters
            .OrderBy(m => m.Name)
            .ToListAsync();
    }

    // ✅ Get a single service by ID
    public async Task<Service?> GetServiceByIdAsync(int serviceId)
    {
        return await _context.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive);
    }

    // ✅ Get a single booking by ID
    public async Task<Booking?> GetBookingByIdAsync(int bookingId)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.Master)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId);
    }

    public async Task<bool> CancelBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking == null)
            return false;

        // 🕓 Find the corresponding time slot
        var slot = await _context.TimeSlots
            .FirstOrDefaultAsync(ts => ts.Date == booking.Date && ts.StartTime == booking.TimeSlot);

        if (slot != null)
        {
            slot.IsOccupied = false; // free up the slot
            _context.TimeSlots.Update(slot);
        }

        _context.Bookings.Remove(booking); // cascade deletes BookingServices
        await _context.SaveChangesAsync();

        // Prepare message info
        var username = string.IsNullOrWhiteSpace(booking.User.Username) ? "Не указан" : booking.User.Username;
        var phone = string.IsNullOrWhiteSpace(booking.User.Phone) ? "Не указан" : booking.User.Phone;
        var servicesText = booking.BookingServices.Any()
            ? string.Join(", ", booking.BookingServices.Select(bs => bs.Service?.Name ?? "Не указано"))
            : "Нет услуг";

        // Message to admin
        var adminMessage = "\u274c Отменена запись!\n" +
                           $"👤 Клиент: @{username}\n" +
                           $"📞 Телефон: {phone}\n" +
                           $"💇 Услуга: {servicesText}\n" +
                           $"📅 Дата: {booking.Date:dd.MM.yyyy}\n" +
                           $"⏰ Время: {booking.TimeSlot:hh\\:mm}";

        try
        {
            
            foreach (var adminId in _adminBot.AdminChatIds)
                await _adminBot.Client.SendTextMessageAsync(
                    adminId,
                    adminMessage
                );
            foreach (var adminId in _adminBot.AdminChatIdsForChannelMessageUpdate)
            {
                await adminRepo.SendAllFreeSlotsAsync(adminId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки уведомления администратору: {ex.Message}");
        }

        // Optionally notify the user
        var userMessage = $"❌ Ваша запись на {booking.Date:dd.MM.yyyy} в {booking.TimeSlot:hh\\:mm} была отменена.";
        try
        {
            await _userBotClient.Client.SendTextMessageAsync(
                booking.UserId, // user's Telegram ID
                userMessage
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки уведомления пользователю: {ex.Message}");
        }

        return true;
    }
    public async Task<bool> CancelReminderBookingAsync(int bookingId)
    {
        var booking = await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking == null)
            return false;

        // 🕓 Find the corresponding time slot
        var slot = await _context.TimeSlots
            .FirstOrDefaultAsync(ts => ts.Date == booking.Date && ts.StartTime == booking.TimeSlot);

        if (slot != null)
        {
            slot.IsOccupied = false; // free up the slot
            _context.TimeSlots.Update(slot);
        }

        _context.Bookings.Remove(booking); // cascade deletes BookingServices
        await _context.SaveChangesAsync();

        // Prepare message info
        var username = string.IsNullOrWhiteSpace(booking.User.Username) ? "Не указан" : booking.User.Username;
        var phone = string.IsNullOrWhiteSpace(booking.User.Phone) ? "Не указан" : booking.User.Phone;
        var servicesText = booking.BookingServices.Any()
            ? string.Join(", ", booking.BookingServices.Select(bs => bs.Service?.Name ?? "Не указано"))
            : "Нет услуг";

        // Message to admin
        var adminMessage = "\u274c Отменена запись!\n" +
                           $"👤 Клиент: @{username}\n" +
                           $"📞 Телефон: {phone}\n" +
                           $"💇 Услуга: {servicesText}\n" +
                           $"📅 Дата: {booking.Date:dd.MM.yyyy}\n" +
                           $"⏰ Время: {booking.TimeSlot:hh\\:mm}";

        try
        {

            foreach (var adminId in _adminBot.AdminChatIds)
                await _adminBot.Client.SendTextMessageAsync(
                    adminId,
                    adminMessage
                );
            foreach (var adminId in _adminBot.AdminChatIdsForChannelMessageUpdate)
            {
                await adminRepo.SendAllFreeSlotsAsync(adminId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка отправки уведомления администратору: {ex.Message}");
        }

        return true;
    }

    public async Task<List<TimeSlot>> GetActiveTimeSlotsAsync(DayOfWeek dayOfWeek)
    {
        return await _context.TimeSlots
            .OrderBy(ts => ts.Date)
            .ThenBy(ts => ts.StartTime)
            .ToListAsync();
    }

    public async Task<List<TimeSlot>> GetActiveTimeSlotsForDayAsync(DateTime date)
    {
        return await _context.TimeSlots
            .Where(ts => ts.IsActive
                         && ts.Date.Date == date.Date
                         && !ts.IsOccupied) // 👈 show only free slots
            .OrderBy(ts => ts.StartTime)
            .ToListAsync();
    }

    public async Task<List<TimeSlot>> GetActiveTimeSlotsForRangeAsync(DateTime start, DateTime end)
    {
        return await _context.TimeSlots
            .Where(ts => ts.IsActive && ts.Date.Date >= start.Date && ts.Date.Date <= end.Date && !ts.IsOccupied)
            .ToListAsync();
    }
    public async Task<Models.User> GetOrCreateUserAsync(User telegramUser)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == telegramUser.Id);

        if (user == null)
        {
            user = new Models.User
            {
                Id = telegramUser.Id,
                Username = telegramUser.Username ?? $"{telegramUser.FirstName} {telegramUser.LastName}",
                CreatedAt = DateTime.Now
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }

        return user;
    }
    public async Task UpdateUserPhoneAsync(long userId, string phoneNumber)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return;

        user.Phone = phoneNumber;
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }
    public async Task<List<Booking>> GetBookingsForReminderAsync(DateTime from, DateTime to)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Include(b => b.BookingServices)
            .ThenInclude(bs => bs.Service)
            .Where(b => !b.ReminderSent)
            .ToListAsync();
    }
    public async Task MarkReminderSentAsync(Booking booking, DateTime sentAt)
    {
        booking.ReminderSent = true;
        booking.ReminderSentAt = sentAt;

        _context.Bookings.Update(booking);
        await _context.SaveChangesAsync();
    }
    public async Task<List<Booking>> GetBookingsForAutoCancelAsync(DateTime olderThan)
    {
        return await _context.Bookings
            .Include(b => b.User)
            .Where(b => b.ReminderSent)
            .Where(b => !b.ReminderConfirmed)
            .Where(b => b.ReminderSentAt < olderThan)
            .ToListAsync();
    }
    public async Task AutoCancelBookingAsync(Booking booking)
    {
        _context.Bookings.Remove(booking);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateBookingAsync(Booking booking)
    {
        _context.Bookings.Update(booking);
        await _context.SaveChangesAsync();
    }
    // 🔹 Get the only studio (Id = 1)
    public async Task<Studio> GetStudioAsync()
    {
        return await _context.Studios.FirstAsync(s => s.Id == 1);
    }

    // 🔹 Update studio (address, name, phone, etc.)
    public async Task UpdateStudioAsync(Studio studio)
    {
        _context.Studios.Update(studio);
        await _context.SaveChangesAsync();
    }





}