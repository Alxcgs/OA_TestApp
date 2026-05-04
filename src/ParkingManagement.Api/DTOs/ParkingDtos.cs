using ParkingManagement.Api.Models;

namespace ParkingManagement.Api.DTOs;

public record EntryRequest(string LicensePlate, VehicleType VehicleType);

public record TicketResponse(
    int Id,
    int ParkingLotId,
    string LicensePlate,
    VehicleType VehicleType,
    DateTime EntryTime,
    DateTime? ExitTime,
    decimal? AmountCharged,
    bool IsPaid
);

public record LotResponse(
    int Id,
    string Name,
    int TotalSpaces,
    decimal HourlyRate,
    string Address,
    int AvailableSpaces
);

public record LotDetailsResponse(
    int Id,
    string Name,
    int TotalSpaces,
    decimal HourlyRate,
    string Address,
    int AvailableSpaces,
    int OccupiedSpaces,
    Dictionary<string, int> AvailableByType
);

public record ErrorResponse(string Message);
