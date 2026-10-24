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
using Microsoft.AspNetCore.Authorization;

namespace PtixiakiReservations.Controllers;

// Handles operations for individual seats and layout units
public class SeatController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    // Retrieves seat data for a layout, filtering availability by active event reservations
    [Authorize]
    [HttpGet]
    public JsonResult get_data(int? LayoutId, int? eventId)
    {
        if (LayoutId == null) return Json(new { });

        var seats = context.Seat
            .Where(s => s.LayoutId == LayoutId)
            .ToList();

        List<int> reservedSeatIds = new List<int>();
        if (eventId.HasValue)
        {
            reservedSeatIds = context.Reservation
                .Where(r => r.EventId == eventId.Value)
                .Select(r => r.SeatId)
                .ToList();
        }

        var seatViewModels = seats.Select(seat => new
        {
            id = seat.Id,
            name = seat.Name,
            x = (float)seat.X,
            y = (float)seat.Y,
            available = seat.Available && !reservedSeatIds.Contains(seat.Id)
        }).ToList();

        return Json(seatViewModels);
    }

    // DTO for seat updates
    public class SeatUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
    }

    // Updates coordinates and name of an existing seat
    [HttpPost]
    [ValidateAntiForgeryToken] 
    [Authorize(Roles = "Admin")]
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

    // Retrieves detailed view for a single seat
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var seat = await context.Seat
            .Include(t => t.Layout.Venue)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (seat == null)
        {
            return NotFound();
        }

        return View(seat);
    }

    // Retrieves seat data as JSON
    [Authorize(Roles = "Admin")]
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

    // Renders the seat creation interface
    [Authorize(Roles = "Admin")]
    public IActionResult Create(int? layoutId)
    {
        if (layoutId == null)
            return NotFound();

        var existingSeats = context.Seat.Any(s => s.LayoutId == layoutId);

        ViewData["LayoutId"] = layoutId.Value;
        ViewData["HasExistingSeats"] = existingSeats;

        return View("CreateSeatMap");
    }

    // Completely replaces all seats in a layout with a newly provided array
    [HttpPost]     
    [Route("Seat/CreateTableMap")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateTableMap(int layoutId, [FromBody] List<Seat> layoutElements)
    {
        try
        {
            if (layoutElements == null || !layoutElements.Any()) 
            {
                return BadRequest("No seat data provided");
            }

            if (layoutId <= 0)
            {
                return BadRequest("Invalid layout ID");
            }

            var layout = await context.Layout.FindAsync(layoutId);
            if (layout == null)
            {
                return NotFound("Layout not found");
            }

            var existingSeats = await context.Seat
                .Where(s => s.LayoutId == layoutId)
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
                    LayoutId = layoutId,
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
    
    // Renders the seat edit form
    [Authorize(Roles = "Admin")]
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
        ViewData["shopID"] = new SelectList(context.Venue, "ID", "ID", seat.LayoutId);
        return View(seat);
    }

    // Processes standard seat form edits
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
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
        ViewData["VenueId"] = new SelectList(context.Venue, "ID", "ID", Seat.LayoutId);
        return View(Seat);
    }

    // Toggles the base availability state of a seat
    [Authorize]
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
        return RedirectToAction("ListOfMySeats", "Seat", new { layoutId = Seat.LayoutId });
    }

    // Bulk deletes selected seats by name
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteMultipleSeats([FromBody] DeleteMultipleSeatsRequest request)
    {
        var seatsToRemove = await context.Seat
            .Where(s => request.seatNames.Contains(s.Name) && s.LayoutId == request.layoutId)
            .ToListAsync();

        if (!seatsToRemove.Any())
            return Json(new { success = false, message = "No matching seats found to delete." });

        context.Seat.RemoveRange(seatsToRemove);
        await context.SaveChangesAsync();

        return Json(new { success = true });
    }
    
    // Renders the seat deletion confirmation view
    [Authorize(Roles = "Admin")]
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

    // Permanently removes a seat and its active reservations
    [Authorize(Roles = "Admin")]
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

    // Verifies seat existence
    [Authorize]
    private bool SeatExists(int id)
    {
        return context.Seat.Any(e => e.Id == id);
    }

    // DTO for bulk seat deletion
    public class DeleteMultipleSeatsRequest
    {
        public List<string> seatNames { get; set; }
        public int layoutId { get; set; }
    }

    // Replaces the entire layout schema (seats, shapes, groups) transactionally
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Route("Seat/SaveCompleteLayout")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveCompleteLayout(int layoutId, [FromBody] LayoutPayloadViewModel payload)
    {
        if (payload == null) return BadRequest("Invalid layout data.");

        using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var existingSeats = context.Seat.Where(s => s.LayoutId == layoutId);
            var existingShapes = context.NonSelectable.Where(s => s.LayoutId == layoutId);
            var existingGroups = context.UnitGroup.Where(g => g.LayoutId == layoutId);

            context.Seat.RemoveRange(existingSeats);
            context.NonSelectable.RemoveRange(existingShapes);
            context.UnitGroup.RemoveRange(existingGroups);
            await context.SaveChangesAsync();

            foreach (var seatVm in payload.Seats)
            {
                context.Seat.Add(new Seat
                {
                    Name = seatVm.Name,
                    X = seatVm.X,
                    Y = seatVm.Y,
                    Width = seatVm.Width,
                    Height = seatVm.Height,
                    LayoutId = layoutId,
                    Available = true
                });
            }

            foreach (var shapeVm in payload.Shapes)
            {
                context.NonSelectable.Add(new NonSelectable
                {
                    Name = shapeVm.Name,
                    X = shapeVm.X,
                    Y = shapeVm.Y,
                    Width = shapeVm.Width,
                    Height = shapeVm.Height,
                    LayoutId = layoutId,
                    ShapeType = shapeVm.ShapeType
                });
            }

            foreach (var groupVm in payload.Groups)
            {
                var newGroup = new UnitGroup
                {
                    Name = groupVm.Name,
                    Top = groupVm.Top,
                    Left = groupVm.Left,
                    LayoutId = layoutId,
                    
                    SelectableUnits = groupVm.SelectableUnits.Select(s => new Seat
                    {
                        Name = s.Name,
                        X = s.X,
                        Y = s.Y,
                        Width = s.Width,
                        Height = s.Height,
                        LayoutId = layoutId,
                        Available = true
                    }).ToList(),
                    
                    NonSelectableUnits = groupVm.NonSelectableUnits.Select(s => new NonSelectable
                    {
                        Name = s.Name,
                        X = s.X,
                        Y = s.Y,
                        Width = s.Width,
                        Height = s.Height,
                        LayoutId = layoutId,
                        ShapeType = s.ShapeType
                    }).ToList()
                };
                
                context.UnitGroup.Add(newGroup);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { success = true, message = "Layout saved successfully" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { success = false, message = "Error saving layout: " + ex.Message });
        }
    }

    // Fetches the complete mapped layout combining standalone elements and grouped objects
    [HttpGet]
    [Authorize]
    [Route("Seat/GetCompleteLayout")]
    public async Task<IActionResult> GetCompleteLayout(int layoutId)
    {
        var groupedSeatIds = await context.UnitGroup
            .Where(g => g.LayoutId == layoutId)
            .SelectMany(g => g.SelectableUnits.Select(s => s.Id))
            .ToListAsync();

        var groupedShapeIds = await context.UnitGroup
            .Where(g => g.LayoutId == layoutId)
            .SelectMany(g => g.NonSelectableUnits.Select(s => s.Id))
            .ToListAsync();

        var standaloneSeats = await context.Seat
            .Where(s => s.LayoutId == layoutId && !groupedSeatIds.Contains(s.Id))
            .Select(s => new {
                id = s.Id,
                name = s.Name,
                x = s.X,
                y = s.Y,
                width = s.Width,
                height = s.Height,
                available = s.Available
            }).ToListAsync();

        var standaloneShapes = await context.NonSelectable
            .Where(s => s.LayoutId == layoutId && !groupedShapeIds.Contains(s.Id))
            .Select(s => new {
                id = s.Id,
                name = s.Name,
                x = s.X,
                y = s.Y,
                width = s.Width,
                height = s.Height,
                shapeType = s.ShapeType
            }).ToListAsync();

        var groups = await context.UnitGroup
            .Include(g => g.SelectableUnits)
            .Include(g => g.NonSelectableUnits)
            .Where(g => g.LayoutId == layoutId)
            .Select(g => new {
                id = g.Id,
                name = g.Name,
                top = g.Top,
                left = g.Left,
                selectableUnits = g.SelectableUnits.Select(s => new {
                    id = s.Id,
                    name = s.Name,
                    x = s.X, 
                    y = s.Y,
                    width = s.Width,
                    height = s.Height,
                    available = s.Available
                }),
                nonSelectableUnits = g.NonSelectableUnits.Select(s => new {
                    id = s.Id,
                    name = s.Name,
                    x = s.X,
                    y = s.Y,
                    width = s.Width,
                    height = s.Height,
                    shapeType = s.ShapeType 
                })
            }).ToListAsync();

        return Json(new { seats = standaloneSeats, shapes = standaloneShapes, groups = groups });
    }
}