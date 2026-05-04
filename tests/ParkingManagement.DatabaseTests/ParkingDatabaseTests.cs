using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;
using ParkingManagement.Api.Services;

namespace ParkingManagement.DatabaseTests;

public class ParkingDatabaseTests : IClassFixture<PostgresDbFixture>
{
    private readonly PostgresDbFixture _fixture;

    public ParkingDatabaseTests(PostgresDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<int> CreateLotAsync(int totalSpaces, decimal hourlyRate)
    {
        await using var context = _fixture.CreateContext();

        var lot = new ParkingLot
        {
            Name = "DB Test Lot " + Guid.NewGuid().ToString("N")[..6],
            Address = "DB Test Address",
            TotalSpaces = totalSpaces,
            HourlyRate = hourlyRate,
            ParkingSpaces = Enumerable.Range(1, totalSpaces).Select(i => new ParkingSpace
            {
                SpaceNumber = $"DB-{i:D3}",
                IsOccupied = false
            }).ToList()
        };

        context.ParkingLots.Add(lot);
        await context.SaveChangesAsync();
        return lot.Id;
    }

    [Fact]
    public async Task EntryAndExit_TracksOccupancyInDatabase()
    {
        await _fixture.ResetDatabaseAsync();
        var lotId = await CreateLotAsync(totalSpaces: 3, hourlyRate: 5m);

        await using (var entryContext = _fixture.CreateContext())
        {
            var service = new ParkingService(entryContext);
            await service.RegisterEntryAsync(lotId, new EntryRequest("DBOCC01", VehicleType.Car));
        }

        await using (var verifyOccupiedContext = _fixture.CreateContext())
        {
            var occupied = await verifyOccupiedContext.ParkingSpaces
                .CountAsync(s => s.ParkingLotId == lotId && s.IsOccupied);
            occupied.Should().Be(1);
        }

        int ticketId;
        await using (var ticketContext = _fixture.CreateContext())
        {
            ticketId = await ticketContext.ParkingTickets
                .Where(t => t.ParkingLotId == lotId && t.ExitTime == null)
                .Select(t => t.Id)
                .SingleAsync();
        }

        await using (var exitContext = _fixture.CreateContext())
        {
            var service = new ParkingService(exitContext);
            await service.RegisterExitAsync(ticketId);
        }

        await using var verifyFreedContext = _fixture.CreateContext();
        var occupiedAfterExit = await verifyFreedContext.ParkingSpaces
            .CountAsync(s => s.ParkingLotId == lotId && s.IsOccupied);
        occupiedAfterExit.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentEntries_OnNearlyFullLot_OnlyOneSucceeds()
    {
        await _fixture.ResetDatabaseAsync();
        var lotId = await CreateLotAsync(totalSpaces: 1, hourlyRate: 7m);

        async Task<bool> TryEntryAsync(string plate)
        {
            try
            {
                await using var context = _fixture.CreateContext();
                var service = new ParkingService(context);
                await service.RegisterEntryAsync(lotId, new EntryRequest(plate, VehicleType.Car));
                return true;
            }
            catch
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryEntryAsync("DBCONC01"),
            TryEntryAsync("DBCONC02"));

        results.Count(success => success).Should().Be(1);

        await using var verifyContext = _fixture.CreateContext();
        var occupied = await verifyContext.ParkingSpaces
            .CountAsync(s => s.ParkingLotId == lotId && s.IsOccupied);
        occupied.Should().Be(1);

        var activeTickets = await verifyContext.ParkingTickets
            .CountAsync(t => t.ParkingLotId == lotId && t.ExitTime == null);
        activeTickets.Should().Be(1);
    }

    [Fact]
    public async Task ExitAndPay_PersistsBillingFields()
    {
        await _fixture.ResetDatabaseAsync();
        var lotId = await CreateLotAsync(totalSpaces: 4, hourlyRate: 10m);

        int ticketId;
        await using (var entryContext = _fixture.CreateContext())
        {
            var service = new ParkingService(entryContext);
            var ticket = await service.RegisterEntryAsync(lotId, new EntryRequest("DBBILL01", VehicleType.Car));
            ticketId = ticket.Id;
        }

        await using (var backdateContext = _fixture.CreateContext())
        {
            var ticket = await backdateContext.ParkingTickets.SingleAsync(t => t.Id == ticketId);
            ticket.EntryTime = DateTime.UtcNow.AddHours(-2.5);
            await backdateContext.SaveChangesAsync();
        }

        await using (var exitContext = _fixture.CreateContext())
        {
            var service = new ParkingService(exitContext);
            await service.RegisterExitAsync(ticketId);
        }

        await using (var payContext = _fixture.CreateContext())
        {
            var service = new ParkingService(payContext);
            await service.PayTicketAsync(ticketId);
        }

        await using var verifyContext = _fixture.CreateContext();
        var persisted = await verifyContext.ParkingTickets.SingleAsync(t => t.Id == ticketId);

        persisted.ExitTime.Should().NotBeNull();
        persisted.AmountCharged.Should().Be(30m);
        persisted.IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task SeedData_CreatesAtLeastTenThousandRecordsAcrossEntities()
    {
        await _fixture.ResetDatabaseAsync(seedLargeData: true);

        await using var context = _fixture.CreateContext();
        var lots = await context.ParkingLots.CountAsync();
        var spaces = await context.ParkingSpaces.CountAsync();
        var tickets = await context.ParkingTickets.CountAsync();

        lots.Should().BeGreaterThan(0);
        spaces.Should().BeGreaterThan(0);
        tickets.Should().BeGreaterOrEqualTo(10_000);
        (lots + spaces + tickets).Should().BeGreaterOrEqualTo(10_000);
    }
}
