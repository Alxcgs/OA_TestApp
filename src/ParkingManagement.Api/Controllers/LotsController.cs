using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Services;

namespace ParkingManagement.Api.Controllers;

[ApiController]
[Route("api/lots")]
public class LotsController : ControllerBase
{
    private readonly IParkingService _parkingService;

    public LotsController(IParkingService parkingService)
    {
        _parkingService = parkingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var lots = await _parkingService.GetAllLotsAsync();
        return Ok(lots);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetails(int id)
    {
        var lot = await _parkingService.GetLotDetailsAsync(id);
        if (lot == null)
            return NotFound(new ErrorResponse("Parking lot not found."));

        return Ok(lot);
    }

    [HttpPost("{id}/entry")]
    public async Task<IActionResult> RegisterEntry(int id, [FromBody] EntryRequest request)
    {
        try
        {
            var ticket = await _parkingService.RegisterEntryAsync(id, request);
            return CreatedAtAction(nameof(GetDetails), new { id }, ticket);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse(ex.Message));
        }
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        try
        {
            var history = await _parkingService.GetHistoryAsync(id);
            return Ok(history);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
    }
}
