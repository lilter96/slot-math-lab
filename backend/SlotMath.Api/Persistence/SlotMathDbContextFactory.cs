using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SlotMath.Api.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef migrations` commands.
/// Reads the connection string from configuration.
/// </summary>
public class SlotMathDbContextFactory : IDesignTimeDbContextFactory<SlotMathDbContext>
{
    public SlotMathDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SlotMathDbContext>();
        options.UseNpgsql("Host=localhost;Port=5433;Database=slotmath;Username=slotmath;Password=slotmath");
        return new SlotMathDbContext(options.Options);
    }
}
