using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Models;

namespace ParkingManagement.Api.Services;

public interface IParkingService
{
    Task<IEnumerable<LotResponse>> GetAllLotsAsync();
    Task<LotDetailsResponse?> GetLotDetailsAsync(int lotId);
    Task<TicketResponse> RegisterEntryAsync(int lotId, EntryRequest request);
    Task<TicketResponse> RegisterExitAsync(int ticketId);
    Task<TicketResponse> PayTicketAsync(int ticketId);
    Task<IEnumerable<TicketResponse>> GetHistoryAsync(int lotId);
    decimal CalculateCost(DateTime entryTime, DateTime exitTime, decimal hourlyRate);
    int GetRequiredSpaces(VehicleType vehicleType);
}
