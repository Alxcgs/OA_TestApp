using AutoFixture;
using AutoFixture.Xunit2;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;
using ParkingManagement.Api.Services;

namespace ParkingManagement.UnitTests;

public class CostCalculationTests
{
    private readonly ParkingService _service;

    public CostCalculationTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ParkingDbContext(options);
        _service = new ParkingService(context);
    }

    [Fact]
    public void CalculateCost_LessThanOneHour_ChargesMinimumOneHour()
    {
        // Arrange
        var entry = DateTime.UtcNow;
        var exit = entry.AddMinutes(30);
        var hourlyRate = 10m;

        // Act
        var cost = _service.CalculateCost(entry, exit, hourlyRate);

        // Assert
        cost.Should().Be(10m); // 1 hour minimum
    }

    [Fact]
    public void CalculateCost_ExactlyOneHour_ChargesOneHour()
    {
        var entry = DateTime.UtcNow;
        var exit = entry.AddHours(1);
        var hourlyRate = 10m;

        var cost = _service.CalculateCost(entry, exit, hourlyRate);

        cost.Should().Be(10m);
    }

    [Fact]
    public void CalculateCost_TwoAndHalfHours_ChargesThreeHours()
    {
        var entry = DateTime.UtcNow;
        var exit = entry.AddHours(2).AddMinutes(30);
        var hourlyRate = 10m;

        var cost = _service.CalculateCost(entry, exit, hourlyRate);

        cost.Should().Be(30m); // ceil(2.5) = 3 hours
    }

    [Fact]
    public void CalculateCost_TwentyFourHours_ChargesTwentyFourHours()
    {
        var entry = DateTime.UtcNow;
        var exit = entry.AddHours(24);
        var hourlyRate = 5m;

        var cost = _service.CalculateCost(entry, exit, hourlyRate);

        cost.Should().Be(120m); // 24 × 5
    }

    [Theory]
    [InlineData(15, 5, 5)]   // 15 min → 1h × 5 = 5
    [InlineData(61, 5, 10)]  // 61 min → 2h × 5 = 10
    [InlineData(180, 10, 30)] // 180 min = 3h × 10 = 30
    [InlineData(1, 100, 100)] // 1 min → 1h × 100 = 100
    public void CalculateCost_VariousDurations_CalculatesCorrectly(int minutes, decimal rate, decimal expected)
    {
        var entry = DateTime.UtcNow;
        var exit = entry.AddMinutes(minutes);

        var cost = _service.CalculateCost(entry, exit, rate);

        cost.Should().Be(expected);
    }

    [Fact]
    public void CalculateCost_FewSeconds_ChargesMinimumOneHour()
    {
        var entry = DateTime.UtcNow;
        var exit = entry.AddSeconds(10);
        var hourlyRate = 8m;

        var cost = _service.CalculateCost(entry, exit, hourlyRate);

        cost.Should().Be(8m);
    }
}
