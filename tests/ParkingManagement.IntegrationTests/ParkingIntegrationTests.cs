using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;

namespace ParkingManagement.IntegrationTests;

public class ParkingIntegrationTests : IClassFixture<ParkingApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;
    private readonly ParkingApiFactory _factory;

    public ParkingIntegrationTests(ParkingApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string UniquePlate(string prefix)
    {
        var cleanPrefix = new string(prefix.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleanPrefix.Length > 10)
            cleanPrefix = cleanPrefix[..10];

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"{cleanPrefix}-{suffix}";
    }

    private async Task<int> SeedLotAsync(int totalSpaces = 10, decimal hourlyRate = 5m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkingDbContext>();
        await db.Database.EnsureCreatedAsync();

        var lot = new ParkingLot
        {
            Name = "Test Lot " + Guid.NewGuid().ToString("N")[..6],
            Address = "Test Address",
            TotalSpaces = totalSpaces,
            HourlyRate = hourlyRate,
            ParkingSpaces = Enumerable.Range(1, totalSpaces).Select(i => new ParkingSpace
            {
                SpaceNumber = $"S-{Guid.NewGuid().ToString("N")[..6]}-{i:D3}",
                IsOccupied = false
            }).ToList()
        };

        db.ParkingLots.Add(lot);
        await db.SaveChangesAsync();
        return lot.Id;
    }

    private async Task<TicketResponse> CreateEntryAsync(int lotId, string plate, VehicleType type)
    {
        var entryResponse = await _client.PostAsJsonAsync(
            $"/api/lots/{lotId}/entry",
            new EntryRequest(plate, type));

        entryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await entryResponse.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
        ticket.Should().NotBeNull();

        return ticket!;
    }

    [Fact]
    public async Task PreseededDatabase_ContainsAtLeastTenThousandRecordsAcrossEntities()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkingDbContext>();
        await db.Database.EnsureCreatedAsync();

        var lots = await db.ParkingLots.CountAsync();
        var spaces = await db.ParkingSpaces.CountAsync();
        var tickets = await db.ParkingTickets.CountAsync();

        lots.Should().BeGreaterThan(0);
        spaces.Should().BeGreaterThan(0);
        tickets.Should().BeGreaterOrEqualTo(10_000);
        (lots + spaces + tickets).Should().BeGreaterOrEqualTo(10_000);
    }

    [Fact]
    public async Task FullFlow_EntryExitPay_ReturnsCorrectResponses()
    {
        var lotId = await SeedLotAsync(10, 10m);
        var plate = UniquePlate("FLOW");

        var ticket = await CreateEntryAsync(lotId, plate, VehicleType.Car);

        var exitResponse = await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);
        exitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var exitTicket = await exitResponse.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
        exitTicket.Should().NotBeNull();
        exitTicket!.ExitTime.Should().NotBeNull();
        exitTicket.AmountCharged.Should().BeGreaterThan(0);

        var payResponse = await _client.PostAsync($"/api/tickets/{ticket.Id}/pay", null);
        payResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paidTicket = await payResponse.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
        paidTicket.Should().NotBeNull();
        paidTicket!.IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task Exit_ChargesRoundedUpHoursTimesRate()
    {
        var lotId = await SeedLotAsync(10, 10m);
        var plate = UniquePlate("COST");
        var ticket = await CreateEntryAsync(lotId, plate, VehicleType.Car);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ParkingDbContext>();
            var ticketEntity = await db.ParkingTickets.SingleAsync(t => t.Id == ticket.Id);
            ticketEntity.EntryTime = DateTime.UtcNow.AddHours(-2.5);
            await db.SaveChangesAsync();
        }

        var exitResponse = await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);
        exitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var exitTicket = await exitResponse.Content.ReadFromJsonAsync<TicketResponse>(JsonOptions);
        exitTicket.Should().NotBeNull();
        exitTicket!.AmountCharged.Should().Be(30m);
    }

    [Fact]
    public async Task Entry_FullParkingLot_ReturnsConflictWithMessage()
    {
        var lotId = await SeedLotAsync(1, 5m);

        await CreateEntryAsync(lotId, UniquePlate("FULLA"), VehicleType.Car);

        var secondEntry = await _client.PostAsJsonAsync(
            $"/api/lots/{lotId}/entry",
            new EntryRequest(UniquePlate("FULLB"), VehicleType.Car));

        secondEntry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await secondEntry.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Message.Should().Contain("No available spaces");
    }

    [Fact]
    public async Task Entry_WithUnpaidTicket_ReturnsConflict()
    {
        var lotId = await SeedLotAsync(10, 5m);
        var plate = UniquePlate("UNPAID");

        var ticket = await CreateEntryAsync(lotId, plate, VehicleType.Car);
        await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);

        var reEntry = await _client.PostAsJsonAsync(
            $"/api/lots/{lotId}/entry",
            new EntryRequest(plate, VehicleType.Car));

        reEntry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await reEntry.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().Contain("unpaid");
    }

    [Fact]
    public async Task PayBeforeExit_ReturnsBadRequest()
    {
        var lotId = await SeedLotAsync(10, 5m);
        var ticket = await CreateEntryAsync(lotId, UniquePlate("PAYBEFORE"), VehicleType.Car);

        var payResponse = await _client.PostAsync($"/api/tickets/{ticket.Id}/pay", null);

        payResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await payResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().Contain("Register exit first");
    }

    [Fact]
    public async Task PayAlreadyPaid_ReturnsBadRequest()
    {
        var lotId = await SeedLotAsync(10, 5m);
        var ticket = await CreateEntryAsync(lotId, UniquePlate("PAYTWICE"), VehicleType.Car);

        await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);
        await _client.PostAsync($"/api/tickets/{ticket.Id}/pay", null);

        var secondPay = await _client.PostAsync($"/api/tickets/{ticket.Id}/pay", null);

        secondPay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await secondPay.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().Contain("already paid");
    }

    [Fact]
    public async Task Exit_UnknownTicket_ReturnsNotFound()
    {
        var response = await _client.PostAsync("/api/tickets/99999999/exit", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task Exit_AlreadyExitedTicket_ReturnsConflict()
    {
        var lotId = await SeedLotAsync(10, 5m);
        var ticket = await CreateEntryAsync(lotId, UniquePlate("EXIT2"), VehicleType.Car);

        var firstExit = await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);
        firstExit.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondExit = await _client.PostAsync($"/api/tickets/{ticket.Id}/exit", null);
        secondExit.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = await secondExit.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error!.Message.Should().Contain("already exited");
    }

    [Fact]
    public async Task GetLots_ReturnsLots()
    {
        await SeedLotAsync(5, 3m);

        var response = await _client.GetAsync("/api/lots");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var lots = await response.Content.ReadFromJsonAsync<List<LotResponse>>(JsonOptions);
        lots.Should().NotBeNull();
        lots.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetLotDetails_ExistingLot_ReturnsDetails()
    {
        var lotId = await SeedLotAsync(20, 7m);

        var response = await _client.GetAsync($"/api/lots/{lotId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var details = await response.Content.ReadFromJsonAsync<LotDetailsResponse>(JsonOptions);
        details.Should().NotBeNull();
        details!.TotalSpaces.Should().Be(20);
        details.AvailableSpaces.Should().Be(20);
        details.AvailableByType["Truck"].Should().Be(10);
    }

    [Fact]
    public async Task GetHistory_ReturnsTicketHistory()
    {
        var lotId = await SeedLotAsync(10, 5m);
        var plate = UniquePlate("HIST");

        await CreateEntryAsync(lotId, plate, VehicleType.Motorcycle);

        var response = await _client.GetAsync($"/api/lots/{lotId}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await response.Content.ReadFromJsonAsync<List<TicketResponse>>(JsonOptions);
        history.Should().NotBeNull();
        history.Should().NotBeEmpty();
        history!.Any(t => t.LicensePlate == plate).Should().BeTrue();
    }
}
