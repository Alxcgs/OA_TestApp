using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParkingManagement.Api.Data;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;

namespace ParkingManagement.Api.Services;

public class ParkingService : IParkingService
{
    private readonly ParkingDbContext _context;

    public ParkingService(ParkingDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<LotResponse>> GetAllLotsAsync()
    {
        var lots = await _context.ParkingLots
            .Include(l => l.ParkingSpaces)
            .ToListAsync();

        return lots.Select(l => new LotResponse(
            l.Id,
            l.Name,
            l.TotalSpaces,
            l.HourlyRate,
            l.Address,
            l.ParkingSpaces.Count(s => !s.IsOccupied)
        ));
    }

    public async Task<LotDetailsResponse?> GetLotDetailsAsync(int lotId)
    {
        var lot = await _context.ParkingLots
            .Include(l => l.ParkingSpaces)
            .FirstOrDefaultAsync(l => l.Id == lotId);

        if (lot == null) return null;

        var availableSpaces = lot.ParkingSpaces.Count(s => !s.IsOccupied);
        var availableByType = new Dictionary<string, int>
        {
            ["Car"] = availableSpaces,
            ["Motorcycle"] = availableSpaces,
            ["Truck"] = availableSpaces / 2
        };

        return new LotDetailsResponse(
            lot.Id,
            lot.Name,
            lot.TotalSpaces,
            lot.HourlyRate,
            lot.Address,
            availableSpaces,
            lot.ParkingSpaces.Count(s => s.IsOccupied),
            availableByType
        );
    }

    public async Task<TicketResponse> RegisterEntryAsync(int lotId, EntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
            throw new InvalidOperationException("License plate is required.");

        var normalizedLicensePlate = NormalizeLicensePlate(request.LicensePlate);

        return await ExecuteWithRetryAsync(async () =>
        {
            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    var lotExists = await _context.ParkingLots.AnyAsync(l => l.Id == lotId);
                    if (!lotExists)
                        throw new KeyNotFoundException($"Parking lot with ID {lotId} not found.");

                    var hasActiveTicket = await _context.ParkingTickets
                        .AnyAsync(t => t.LicensePlate == normalizedLicensePlate && t.ExitTime == null);

                    if (hasActiveTicket)
                        throw new InvalidOperationException("Vehicle already has an active parking ticket.");

                    var hasUnpaidTicket = await _context.ParkingTickets
                        .AnyAsync(t => t.LicensePlate == normalizedLicensePlate && !t.IsPaid && t.ExitTime != null);

                    if (hasUnpaidTicket)
                        throw new InvalidOperationException("Vehicle has an unpaid parking ticket. Please pay before re-entering.");

                    var requiredSpaces = GetRequiredSpaces(request.VehicleType);

                    List<ParkingSpace> availableSpaces;
                    if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
                    {
                        availableSpaces = await _context.ParkingSpaces
                            .Where(s => s.ParkingLotId == lotId && !s.IsOccupied)
                            .OrderBy(s => s.Id)
                            .Take(requiredSpaces)
                            .ToListAsync();
                    }
                    else
                    {
                        availableSpaces = await _context.ParkingSpaces
                            .FromSqlInterpolated($"SELECT * FROM \"ParkingSpaces\" WHERE \"ParkingLotId\" = {lotId} AND \"IsOccupied\" = FALSE ORDER BY \"Id\" LIMIT {requiredSpaces} FOR UPDATE SKIP LOCKED")
                            .ToListAsync();
                    }

                    if (availableSpaces.Count < requiredSpaces)
                        throw new InvalidOperationException($"No available spaces for {request.VehicleType}. Required: {requiredSpaces}, Available: {availableSpaces.Count}.");

                    var ticket = new ParkingTicket
                    {
                        ParkingLotId = lotId,
                        LicensePlate = normalizedLicensePlate,
                        VehicleType = request.VehicleType,
                        EntryTime = DateTime.UtcNow,
                        IsPaid = false
                    };

                    _context.ParkingTickets.Add(ticket);

                    foreach (var space in availableSpaces)
                    {
                        space.IsOccupied = true;
                        space.VehicleType = request.VehicleType;
                    }

                    // First save: persists ticket (assigns ticket.Id) and marks spaces occupied.
                    await _context.SaveChangesAsync();

                    // Second save: links spaces to the now-known ticket.Id.
                    // This is a minimal follow-up write — the heavy contended work is already done.
                    foreach (var space in availableSpaces)
                        space.CurrentTicketId = ticket.Id;

                    await _context.SaveChangesAsync();

                    return MapToResponse(ticket);
                });
            }
            catch (DbUpdateException ex) when (IsActiveTicketUniqueViolation(ex))
            {
                throw new InvalidOperationException("Vehicle already has an active parking ticket.");
            }
        });
    }

    public async Task<TicketResponse> RegisterExitAsync(int ticketId)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            return await ExecuteInTransactionAsync(async () =>
            {
                ParkingTicket ticket;
                if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
                {
                    ticket = await _context.ParkingTickets
                        .FirstOrDefaultAsync(t => t.Id == ticketId);
                }
                else
                {
                    ticket = await _context.ParkingTickets
                        .FromSqlInterpolated($"SELECT * FROM \"ParkingTickets\" WHERE \"Id\" = {ticketId} FOR UPDATE")
                        .FirstOrDefaultAsync();
                }

                if (ticket == null)
                    throw new KeyNotFoundException($"Ticket with ID {ticketId} not found.");

                await _context.Entry(ticket).Reference(t => t.ParkingLot).LoadAsync();

                if (ticket.ExitTime != null)
                    throw new InvalidOperationException("Vehicle has already exited.");

                ticket.ExitTime = DateTime.UtcNow;
                ticket.AmountCharged = CalculateCost(ticket.EntryTime, ticket.ExitTime.Value, ticket.ParkingLot.HourlyRate);

                var requiredSpaces = GetRequiredSpaces(ticket.VehicleType);
                var occupiedSpaces = await _context.ParkingSpaces
                    .Where(s => s.ParkingLotId == ticket.ParkingLotId && s.CurrentTicketId == ticket.Id)
                    .OrderBy(s => s.Id)
                    .Take(requiredSpaces)
                    .ToListAsync();

                // If fewer spaces are linked than expected, release whatever we find
                // (DB may be in a partially-updated state; do not block the exit).

                foreach (var space in occupiedSpaces)
                {
                    space.IsOccupied = false;
                    space.VehicleType = null;
                    space.CurrentTicketId = null;
                }

                await _context.SaveChangesAsync();

                return MapToResponse(ticket);
            });
        });
    }

    public async Task<TicketResponse> PayTicketAsync(int ticketId)
    {
        var ticket = await _context.ParkingTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId);

        if (ticket == null)
            throw new KeyNotFoundException($"Ticket with ID {ticketId} not found.");

        if (ticket.IsPaid)
            throw new InvalidOperationException("Ticket is already paid.");

        if (ticket.ExitTime == null)
            throw new InvalidOperationException("Vehicle has not exited yet. Register exit first.");

        ticket.IsPaid = true;
        await _context.SaveChangesAsync();

        return MapToResponse(ticket);
    }

    public async Task<IEnumerable<TicketResponse>> GetHistoryAsync(int lotId)
    {
        var lotExists = await _context.ParkingLots.AnyAsync(l => l.Id == lotId);
        if (!lotExists)
            throw new KeyNotFoundException($"Parking lot with ID {lotId} not found.");

        var tickets = await _context.ParkingTickets
            .Where(t => t.ParkingLotId == lotId)
            .OrderByDescending(t => t.EntryTime)
            .ToListAsync();

        return tickets.Select(MapToResponse);
    }

    public decimal CalculateCost(DateTime entryTime, DateTime exitTime, decimal hourlyRate)
    {
        var totalHours = (exitTime - entryTime).TotalHours;
        var billedHours = Math.Max(1, (int)Math.Ceiling(totalHours));
        return billedHours * hourlyRate;
    }

    public int GetRequiredSpaces(VehicleType vehicleType)
    {
        return vehicleType == VehicleType.Truck ? 2 : 1;
    }

    private async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action)
    {
        if (!_context.Database.IsRelational())
            return await action();

        // Using ReadCommitted since we use FOR UPDATE SKIP LOCKED to prevent concurrent grabs
        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        try
        {
            var result = await action();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxAttempts = 5)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsSerializationConflict(ex) && attempt < maxAttempts - 1)
            {
                attempt++;
                // Jittered exponential backoff: 10–50ms, 20–100ms, 40–200ms, 80–400ms
                var baseDelay = 10 * (1 << attempt);
                var jitter = Random.Shared.Next(0, baseDelay / 2);
                await Task.Delay(baseDelay + jitter);

                // Clear EF change tracker so retry starts with a clean state
                _context.ChangeTracker.Clear();
            }
        }
    }

    private static string NormalizeLicensePlate(string licensePlate)
    {
        return licensePlate.Trim().ToUpperInvariant();
    }

    private static bool IsActiveTicketUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg
               && pg.SqlState == PostgresErrorCodes.UniqueViolation
               && pg.ConstraintName == "IX_ParkingTickets_ActiveLicensePlate";
    }

    private static bool IsSerializationConflict(Exception ex)
    {
        var queue = new Queue<Exception>();
        queue.Enqueue(ex);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                return true;
            }

            if (current is DbUpdateException dbEx
                && dbEx.InnerException is PostgresException dbPg
                && dbPg.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                return true;
            }

            if (current is AggregateException aggregateEx)
            {
                foreach (var inner in aggregateEx.InnerExceptions)
                {
                    queue.Enqueue(inner);
                }
            }

            if (current.InnerException != null)
            {
                queue.Enqueue(current.InnerException);
            }
        }

        return false;
    }

    private static TicketResponse MapToResponse(ParkingTicket ticket)
    {
        return new TicketResponse(
            ticket.Id,
            ticket.ParkingLotId,
            ticket.LicensePlate,
            ticket.VehicleType,
            ticket.EntryTime,
            ticket.ExitTime,
            ticket.AmountCharged,
            ticket.IsPaid
        );
    }
}
