namespace ParkingManagement.Api.Models;

public class ParkingSpace
{
    public int Id { get; set; }
    public int ParkingLotId { get; set; }
    public string SpaceNumber { get; set; } = string.Empty;
    public bool IsOccupied { get; set; }
    public VehicleType? VehicleType { get; set; }
    public int? CurrentTicketId { get; set; }

    public ParkingLot ParkingLot { get; set; } = null!;
}
