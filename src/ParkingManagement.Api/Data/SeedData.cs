using Bogus;
using ParkingManagement.Api.Models;

namespace ParkingManagement.Api.Data;

public static class SeedData
{
    public static void Initialize(ParkingDbContext context)
    {
        if (context.ParkingLots.Any()) return;

        // Generate 10 parking lots
        var lotFaker = new Faker<ParkingLot>()
            .RuleFor(l => l.Name, f => f.Company.CompanyName() + " Parking")
            .RuleFor(l => l.Address, f => f.Address.FullAddress())
            .RuleFor(l => l.TotalSpaces, f => f.Random.Int(50, 200))
            .RuleFor(l => l.HourlyRate, f => Math.Round(f.Random.Decimal(2m, 15m), 2));

        var lots = lotFaker.Generate(10);
        context.ParkingLots.AddRange(lots);
        context.SaveChanges();

        // Generate parking spaces for each lot
        var allSpaces = new List<ParkingSpace>();
        foreach (var lot in lots)
        {
            for (var i = 1; i <= lot.TotalSpaces; i++)
            {
                allSpaces.Add(new ParkingSpace
                {
                    ParkingLotId = lot.Id,
                    SpaceNumber = $"S-{lot.Id:D2}-{i:D3}",
                    IsOccupied = false,
                    VehicleType = null,
                    CurrentTicketId = null
                });
            }
        }

        context.ParkingSpaces.AddRange(allSpaces);
        context.SaveChanges();

        // Generate 10,000 historical parking tickets across lots.
        var ticketFaker = new Faker<ParkingTicket>()
            .RuleFor(t => t.LicensePlate, f => f.Random.Replace("??-####-??").ToUpper())
            .RuleFor(t => t.ParkingLotId, f => f.PickRandom(lots).Id)
            .RuleFor(t => t.VehicleType, f =>
            {
                var roll = f.Random.Double();
                if (roll < 0.70) return VehicleType.Car;
                if (roll < 0.90) return VehicleType.Motorcycle;
                return VehicleType.Truck;
            })
            .RuleFor(t => t.EntryTime, f => f.Date.Between(DateTime.UtcNow.AddMonths(-6), DateTime.UtcNow.AddHours(-2)))
            .RuleFor(t => t.ExitTime, (f, t) => t.EntryTime.AddMinutes(f.Random.Int(30, 720)))
            .RuleFor(t => t.IsPaid, f => f.Random.Bool(0.85f))
            .RuleFor(t => t.AmountCharged, (f, t) =>
            {
                var lot = lots.First(l => l.Id == t.ParkingLotId);
                var hours = Math.Max(1, (int)Math.Ceiling((t.ExitTime!.Value - t.EntryTime).TotalHours));
                return hours * lot.HourlyRate;
            });

        var tickets = ticketFaker.Generate(10000);

        // Add in batches for better insert speed.
        for (var i = 0; i < tickets.Count; i += 500)
        {
            var batch = tickets.Skip(i).Take(500);
            context.ParkingTickets.AddRange(batch);
            context.SaveChanges();
        }
    }
}
