using AutoFixture;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;
using ParkingManagement.Api.Services;

namespace ParkingManagement.UnitTests;

public class SpaceAvailabilityTests
{
    private readonly Fixture _fixture;

    public SpaceAvailabilityTests()
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
    public async Task RegisterEntry_WithAvailableSpaces_OccupiesOneSpace_AndCreatesActiveTicket()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 10,
            HourlyRate = 5m,
            ParkingSpaces = Enumerable.Range(1, 10).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = false
            }).ToList()
        };
        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var result = await service.RegisterEntryAsync(lot.Id, new EntryRequest("ab1234cd", VehicleType.Car));

        result.LicensePlate.Should().Be("AB1234CD");
        result.VehicleType.Should().Be(VehicleType.Car);

        var occupiedCount = await context.ParkingSpaces.CountAsync(s => s.ParkingLotId == lot.Id && s.IsOccupied);
        occupiedCount.Should().Be(1);

        var ticket = await context.ParkingTickets.SingleAsync(t => t.Id == result.Id);
        ticket.ExitTime.Should().BeNull();
        ticket.IsPaid.Should().BeFalse();
        ticket.LicensePlate.Should().Be("AB1234CD");

        var occupiedSpace = await context.ParkingSpaces.SingleAsync(s => s.CurrentTicketId == result.Id);
        occupiedSpace.VehicleType.Should().Be(VehicleType.Car);
    }

    [Fact]
    public async Task RegisterEntry_NoAvailableSpaces_ThrowsException()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 2,
            HourlyRate = 5m,
            ParkingSpaces = Enumerable.Range(1, 2).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = true,
                VehicleType = VehicleType.Car
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var act = () => service.RegisterEntryAsync(lot.Id, new EntryRequest("XY9999ZZ", VehicleType.Car));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No available spaces*");
    }

    [Fact]
    public async Task RegisterEntry_DuplicateActiveTicket_ThrowsException()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 10,
            HourlyRate = 5m,
            ParkingSpaces = Enumerable.Range(1, 10).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = false
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        await service.RegisterEntryAsync(lot.Id, new EntryRequest("DUP-1234", VehicleType.Car));

        var act = () => service.RegisterEntryAsync(lot.Id, new EntryRequest("dup-1234", VehicleType.Car));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has an active*");
    }

    [Fact]
    public async Task RegisterEntry_UnpaidTicket_ThrowsException()
    {
        var (context, service) = CreateServiceWithDb();
        var lot = new ParkingLot
        {
            Name = _fixture.Create<string>(),
            Address = _fixture.Create<string>(),
            TotalSpaces = 10,
            HourlyRate = 5m,
            ParkingSpaces = Enumerable.Range(1, 10).Select(_ => new ParkingSpace
            {
                SpaceNumber = _fixture.Create<string>(),
                IsOccupied = false
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();

        var ticket = await service.RegisterEntryAsync(lot.Id, new EntryRequest("UNPAID-01", VehicleType.Car));
        await service.RegisterExitAsync(ticket.Id);

        var act = () => service.RegisterEntryAsync(lot.Id, new EntryRequest("UNPAID-01", VehicleType.Car));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unpaid*");
    }
}
