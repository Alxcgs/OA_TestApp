using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Api.DTOs;
using ParkingManagement.Api.Services;

namespace ParkingManagement.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly IParkingService _parkingService;

    public TicketsController(IParkingService parkingService)
    {
        _parkingService = parkingService;
    }

    [HttpPost("{id}/exit")]
    public async Task<IActionResult> RegisterExit(int id)
    {
        try
        {
            var ticket = await _parkingService.RegisterExitAsync(id);
            return Ok(ticket);
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

    [HttpPost("{id}/pay")]
    public async Task<IActionResult> Pay(int id)
    {
        try
        {
            var ticket = await _parkingService.PayTicketAsync(id);
            return Ok(ticket);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse(ex.Message));
        }
    }
}
