namespace ParkingManagement.Api.Models;

public class ParkingTicket
{
    public int Id { get; set; }
    public int ParkingLotId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal? AmountCharged { get; set; }
    public bool IsPaid { get; set; }

    public ParkingLot ParkingLot { get; set; } = null!;
}
