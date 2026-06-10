using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IpManager.Persistence.EfCore.Data;

/// <summary>
/// Lets `dotnet ef migrations add ...` / `dotnet ef database update` build a context at design
/// time without booting the web host. Reads ConnectionStrings:Default from the Web project's
/// appsettings.json (run the ef commands with -s pointing at IpManager.Web).
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = config.GetConnectionString("Default")
                 ?? "Server=(localdb)\\MSSQLLocalDB;Database=AircraftFactoryIpManager;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cs)
            .Options;

        return new AppDbContext(options);
    }
}
