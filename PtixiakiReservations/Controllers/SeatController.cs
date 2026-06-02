using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Net;

namespace PtixiakiReservations.Controllers;

public class SeatController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    : Controller
{
    // GET: Table
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public JsonResult get_data(int? SubAreaId, int? eventId)
    {
        if (SubAreaId == null) return Json(new { });

        // First get all seats for this subarea
        var seats = context.Seat
            .Where(s => s.SubAreaId == SubAreaId)
            .ToList(); // Get the seat entities first

        // If eventId is provided, get all seat IDs that are already reserved for this event
        List<int> reservedSeatIds = new List<int>();
        if (eventId.HasValue)
        {
            reservedSeatIds = context.Reservation
                .Where(r => r.EventId == eventId.Value)
                .Select(r => r.SeatId)
                .ToList();
        }

        // Create a new list of objects with the merged data
        var seatViewModels = seats.Select(seat => new
        {
            id = seat.Id,
            name = seat.Name,
            x = (float)seat.X,
            y = (float)seat.Y,
            available = seat.Available &&
                        !reservedSeatIds.Contains(seat.Id) // Check both the seat's availability and if it's reserved
        }).ToList();

        return Json(seatViewModels);
    }

    public class SeatUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> UpdateSeat([FromBody] SeatUpdateDto request)
    {
        if (request == null || request.Id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid seat data." });
        }

        var seat = await context.Seat.FindAsync(request.Id);
        if (seat == null)
        {
            return NotFound(new { success = false, message = "Seat not found." });
        }

        seat.Name = request.Name;
        seat.X = request.X;
        seat.Y = request.Y;

        try
        {
            context.Update(seat);
            await context.SaveChangesAsync();
            
            return Ok(new { success = true });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new { success = false, message = "Database error occurred while updating the seat." });
        }
    }

    // GET: Table/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var seat = await context.Seat
            .Include(t => t.SubArea.Venue)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (seat == null)
        {
            return NotFound();
        }

        return View(seat);
    }

    public async Task<IActionResult> Single(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var seat = await context.Seat
            .FirstOrDefaultAsync(m => m.Id == id);
        if (seat == null)
        {
            return NotFound();
        }

        return Json(seat);
    }

    // GET: Table/Create
    public IActionResult Create(int? subAreaId)
    {
        if (subAreaId == null)
            return NotFound();

        var existingSeats = context.Seat.Any(s => s.SubAreaId == subAreaId);

        ViewData["SubAreaId"] = subAreaId.Value;
        ViewData["HasExistingSeats"] = existingSeats;

        return View("CreateSeatMap");
    }

    [HttpPost]     
    [Route("Seat/CreateTableMap")]
    public async Task<IActionResult> CreateTableMap(int subAreaId, [FromBody] List<Seat> layoutElements)
    {
        try
        {
            if (layoutElements == null || !layoutElements.Any()) 
            {
                return BadRequest("No seat data provided");
            }

            if (subAreaId <= 0)
            {
                return BadRequest("Invalid layout ID");
            }

            var subArea = await context.SubArea.FindAsync(subAreaId);
            if (subArea == null)
            {
                return NotFound("Layout not found");
            }

            var existingSeats = await context.Seat
                .Where(s => s.SubAreaId == subAreaId)
                .ToListAsync();

            if (existingSeats.Any())
            {
                context.Seat.RemoveRange(existingSeats);
                await context.SaveChangesAsync();
            }

            foreach (var s in layoutElements)
            {
                Seat seat = new Seat
                {
                    Name = s.Name,
                    X = s.X,            
                    Y = s.Y,            
                    Width = s.Width,   
                    Height = s.Height,  
                    SubAreaId = subAreaId,
                    Available = true
                };
                context.Add(seat);
            }

            await context.SaveChangesAsync();
            return Ok(new { success = true, message = "Layout saved successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "An error occurred", error = ex.Message });
        }
    }
    
    // GET: Table/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var seat = await context.Seat.FindAsync(id);
        if (seat == null)
        {
            return NotFound();
        }
        ViewData["shopID"] = new SelectList(context.Venue, "ID", "ID", seat.SubAreaId);
        return View(seat);
    }

    // POST: Table/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("ID,ReservationId,VenueId")] Seat Seat)
    {
        if (id != Seat.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                context.Update(Seat);
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeatExists(Seat.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        ViewData["VenueId"] = new SelectList(context.Venue, "ID", "ID", Seat.SubAreaId);
        return View(Seat);
    }

    public async Task<IActionResult> ChangeAvailable(int ID, bool Flag)
    {
        Seat Seat = context.Seat.FirstOrDefault(t => t.Id == ID);
        if (Seat != null)
        {
            Seat.Available = Flag;
            if (ModelState.IsValid)
            {
                try
                {
                    context.Update(Seat);
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SeatExists(Seat.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        return RedirectToAction("ListOfMySeats", "Seat", new { subAreaId = Seat.SubAreaId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMultipleSeats([FromBody] DeleteMultipleSeatsRequest request)
    {
        var seatsToRemove = await context.Seat
            .Where(s => request.seatNames.Contains(s.Name) && s.SubAreaId == request.subAreaId)
            .ToListAsync();

        if (!seatsToRemove.Any())
            return Json(new { success = false, message = "No matching seats found to delete." });

        context.Seat.RemoveRange(seatsToRemove);
        await context.SaveChangesAsync();

        return Json(new { success = true });
    }
    
    // GET: Table/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var seat = await context.Seat.FirstOrDefaultAsync(m => m.Id == id);

        if (seat == null)
        {
            return NotFound();
        }

        return View(seat);
    }

    // POST: Table/Delete/5
    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var seat = await context.Seat.FindAsync(id);
        var res = context.Reservation
            .Where(r => r.SeatId == seat.Id).ToList();
        if (res.Count != 0)
        {
            foreach (var r in res)
            {
                context.Reservation.Remove(r);
            }
        }
        context.Seat.Remove(seat);
        await context.SaveChangesAsync();

        return RedirectToAction("ListOfMySeats");
    }

    private bool SeatExists(int id)
    {
        return context.Seat.Any(e => e.Id == id);
    }
}

public class DeleteMultipleSeatsRequest
{
    public List<string> seatNames { get; set; }
    public int subAreaId { get; set; }
}