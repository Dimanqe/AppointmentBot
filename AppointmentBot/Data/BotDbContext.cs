#region

using AppointmentBot.Models;
using Microsoft.EntityFrameworkCore;

#endregion

namespace AppointmentBot.Data;

public class BotDbContext : DbContext
{
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Master> Masters { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<TimeSlot> TimeSlots { get; set; }
    public DbSet<BookingService> BookingServices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Composite key for join table
        modelBuilder.Entity<BookingService>()
            .HasKey(bs => new { bs.BookingId, bs.ServiceId });

        modelBuilder.Entity<Service>()
            .Property(s => s.Price)
            .HasColumnType("numeric(10,2)");

        modelBuilder.Entity<BookingService>()
            .HasOne(bs => bs.Booking)
            .WithMany(b => b.BookingServices)
            .HasForeignKey(bs => bs.BookingId);
        //.OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BookingService>()
            .HasOne(bs => bs.Service)
            .WithMany(s => s.BookingServices)
            .HasForeignKey(bs => bs.ServiceId);

        modelBuilder.Entity<User>()
            .Property(u => u.Phone)
            .HasColumnType("text"); // Добавляем Phone в БД

        modelBuilder.Entity<Master>().HasData(
            new Master { Id = 1, Name = "Арина" },
            new Master { Id = 2, Name = "Лиля" }
        );

        modelBuilder.Entity<Service>().HasData(
            new Service { Id = 1, Name = "Классическое наращивание" },
            new Service { Id = 2, Name = "Наращивание 2D" },
            new Service { Id = 3, Name = "Наращивание 3D" },
            new Service { Id = 4, Name = "Наращивание 4D" },
            new Service { Id = 4, Name = "Наращивание 7D" },
            new Service { Id = 5, Name = "Наращивание \"батерфлай\"" },
            new Service { Id = 6, Name = "Наращивание \"американка\"" },
            new Service { Id = 7, Name = "Ламинирование ресниц" }
        );

        // Global DateTime type rule
        foreach (var prop in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
            prop.SetColumnType("timestamp without time zone");
    }
}