namespace ParkingManagement.Api.Models;

public class ParkingLot
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalSpaces { get; set; }
    public decimal HourlyRate { get; set; }
    public string Address { get; set; } = string.Empty;

    public ICollection<ParkingSpace> ParkingSpaces { get; set; } = new List<ParkingSpace>();
    public ICollection<ParkingTicket> ParkingTickets { get; set; } = new List<ParkingTicket>();
}
