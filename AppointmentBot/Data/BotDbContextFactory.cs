#region

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

#endregion

namespace AppointmentBot.Data;

public class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();

        // Use the same PostgreSQL connection string
        var connectionString = Environment.GetEnvironmentVariable("PostgresConnection")
                               ?? "Host=localhost;Port=5432;Database=botdb;Username=postgres;Password=postgres;";

        optionsBuilder.UseNpgsql(connectionString);

        return new BotDbContext(optionsBuilder.Options);
    }
}