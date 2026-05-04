using Microsoft.EntityFrameworkCore;
using ParkingManagement.Api.Models;

namespace ParkingManagement.Api.Data;

public class ParkingDbContext : DbContext
{
    public ParkingDbContext(DbContextOptions<ParkingDbContext> options) : base(options) { }

    public DbSet<ParkingLot> ParkingLots => Set<ParkingLot>();
    public DbSet<ParkingTicket> ParkingTickets => Set<ParkingTicket>();
    public DbSet<ParkingSpace> ParkingSpaces => Set<ParkingSpace>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParkingLot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(500);
            entity.Property(e => e.HourlyRate).HasPrecision(10, 2);
        });

        modelBuilder.Entity<ParkingTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LicensePlate).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AmountCharged).HasPrecision(10, 2);
            entity.Property(e => e.VehicleType)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.HasIndex(e => e.LicensePlate)
                  .IsUnique()
                  .HasDatabaseName("IX_ParkingTickets_ActiveLicensePlate")
                  .HasFilter("\"ExitTime\" IS NULL");

            entity.HasIndex(e => e.ParkingLotId);

            entity.HasOne(e => e.ParkingLot)
                  .WithMany(p => p.ParkingTickets)
                  .HasForeignKey(e => e.ParkingLotId);
        });

        modelBuilder.Entity<ParkingSpace>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SpaceNumber).IsRequired().HasMaxLength(20);
            entity.Property(e => e.VehicleType)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.HasIndex(e => new { e.ParkingLotId, e.SpaceNumber }).IsUnique();
            entity.HasIndex(e => new { e.ParkingLotId, e.IsOccupied });
            entity.HasIndex(e => e.CurrentTicketId);

            entity.HasOne(e => e.ParkingLot)
                  .WithMany(p => p.ParkingSpaces)
                  .HasForeignKey(e => e.ParkingLotId);
        });
    }
}
