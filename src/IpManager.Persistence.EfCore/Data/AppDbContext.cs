using IpManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IpManager.Persistence.EfCore.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<IpAddress> IpAddresses => Set<IpAddress>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<IpBinding> IpBindings => Set<IpBinding>();
    public DbSet<Conflict> Conflicts => Set<Conflict>();
    public DbSet<ConflictDevice> ConflictDevices => Set<ConflictDevice>();
    public DbSet<InternetActivity> InternetActivities => Set<InternetActivity>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<IpAddress>(e =>
        {
            e.HasIndex(x => x.Address).IsUnique();
            e.HasIndex(x => x.IpNumeric);            // numeric ordering / range scans
            e.Property(x => x.Address).HasMaxLength(45);
            e.HasOne(x => x.CurrentDevice).WithMany().HasForeignKey(x => x.CurrentDeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Device>(e =>
        {
            e.HasIndex(x => x.MacNormalized).IsUnique();
            e.Property(x => x.MacNormalized).HasMaxLength(12);
            e.Property(x => x.MacAddress).HasMaxLength(17);
        });

        b.Entity<IpBinding>(e =>
        {
            e.HasIndex(x => new { x.IpAddressId, x.IsActive });
            e.HasIndex(x => new { x.DeviceId, x.IsActive });
            e.HasOne(x => x.IpAddress).WithMany(x => x.Bindings).HasForeignKey(x => x.IpAddressId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Device).WithMany(x => x.Bindings).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Conflict>(e =>
        {
            e.HasIndex(x => new { x.IpAddressId, x.IsResolved });
            e.HasOne(x => x.IpAddress).WithMany().HasForeignKey(x => x.IpAddressId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConflictDevice>(e =>
        {
            e.HasKey(x => new { x.ConflictId, x.DeviceId });
            e.HasOne(x => x.Conflict).WithMany(x => x.Devices).HasForeignKey(x => x.ConflictId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<InternetActivity>(e =>
        {
            e.HasIndex(x => new { x.DeviceId, x.ActivityTime });
            e.HasIndex(x => x.ActivityTime);
        });

        b.Entity<Notification>(e => e.HasIndex(x => x.CreatedAt));
    }
}
