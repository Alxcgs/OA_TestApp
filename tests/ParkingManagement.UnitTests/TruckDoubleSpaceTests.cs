using AutoFixture;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;
using ParkingManagement.Api.Services;

namespace ParkingManagement.UnitTests;

public class TruckDoubleSpaceTests
{
    private readonly Fixture _fixture;

    public TruckDoubleSpaceTests()
    {
        _fixture = new Fixture();
        _fixture.Behaviors.OfType<ThrowingRecursionBehavior>()
            .ToList()
            .ForEach(b => _fixture.Behaviors.Remove(b));
        _fixture.Behaviors.Add(new OmitOnRecursionBehavior());
    }

    private (ParkingDbContext context, ParkingService service) CreateServiceWithDb()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ParkingDbContext(options);
        var service = new ParkingService(context);
        return (context, service);
    }

    [Fact]
    public void GetRequiredSpaces_Truck_ReturnsTwo()
    {
        var (_, service) = CreateServiceWithDb();
        service.GetRequiredSpaces(VehicleType.Truck).Should().Be(2);
    }

    [Fact]
    public void GetRequiredSpaces_Car_ReturnsOne()
    {
        var (_, service) = CreateServiceWithDb();
        service.GetRequiredSpaces(VehicleType.Car).Should().Be(1);
    }

    [Fact]
    public void GetRequiredSpaces_Motorcycle_ReturnsOne()
    {
        var (_, service) = CreateServiceWithDb();
        service.GetRequiredSpaces(VehicleType.Motorcycle).Should().Be(1);
    }

    [Fact]
    public async Task RegisterEntry_Truck_OccupiesTwoSpaces()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 5,
            HourlyRate = 10m,
            ParkingSpaces = Enumerable.Range(1, 5).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = false
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var ticket = await service.RegisterEntryAsync(lot.Id, new EntryRequest("TRUCK-01", VehicleType.Truck));

        var occupiedSpaces = await context.ParkingSpaces
            .Where(s => s.ParkingLotId == lot.Id && s.IsOccupied)
            .ToListAsync();

        occupiedSpaces.Should().HaveCount(2);
        occupiedSpaces.Should().OnlyContain(s => s.VehicleType == VehicleType.Truck);
        occupiedSpaces.Should().OnlyContain(s => s.CurrentTicketId == ticket.Id);
    }

    [Fact]
    public async Task RegisterEntry_TruckWithOnlyOneSpace_ThrowsException()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 3,
            HourlyRate = 10m,
            ParkingSpaces = new List<ParkingSpace>
            {
                new() { SpaceNumber = _fixture.Create<string>(), IsOccupied = true, VehicleType = VehicleType.Car },
                new() { SpaceNumber = _fixture.Create<string>(), IsOccupied = true, VehicleType = VehicleType.Car },
                new() { SpaceNumber = _fixture.Create<string>(), IsOccupied = false }
            }
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var act = () => service.RegisterEntryAsync(lot.Id, new EntryRequest("BIGTRUCK", VehicleType.Truck));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No available spaces*Truck*Required: 2*Available: 1*");
    }

    [Fact]
    public async Task RegisterExit_Truck_FreesTwoSpaces()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 4,
            HourlyRate = 10m,
            ParkingSpaces = Enumerable.Range(1, 4).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = false
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var ticket = await service.RegisterEntryAsync(lot.Id, new EntryRequest("TRUCK-02", VehicleType.Truck));

        var occupiedBefore = await context.ParkingSpaces
            .CountAsync(s => s.ParkingLotId == lot.Id && s.IsOccupied && s.CurrentTicketId == ticket.Id);
        occupiedBefore.Should().Be(2);

        await service.RegisterExitAsync(ticket.Id);

        var occupiedAfter = await context.ParkingSpaces.CountAsync(s => s.ParkingLotId == lot.Id && s.IsOccupied);
        occupiedAfter.Should().Be(0);

        var linkedSpaces = await context.ParkingSpaces
            .Where(s => s.ParkingLotId == lot.Id)
            .Select(s => s.CurrentTicketId)
            .ToListAsync();
        linkedSpaces.Should().OnlyContain(ticketId => ticketId == null);
    }
}
