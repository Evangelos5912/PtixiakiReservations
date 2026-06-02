using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace PtixiakiReservations.Controllers;

public class NonSelectableController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    : Controller
{
    // 1. MASTER SAVE (Matches: fetch('/NonSelectable/SaveElements'))
    [HttpPost]
    public async Task<IActionResult> SaveElements(int subAreaId, [FromBody] List<NonSelectable> elements)
    {
        var existing = await context.NonSelectable
            .Where(e => e.SubAreaId == subAreaId)
            .ToListAsync();

        context.NonSelectable.RemoveRange(existing);
        
        foreach (var el in elements)
        {
            el.SubAreaId = subAreaId;
            context.NonSelectable.Add(el);
        }

        await context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // 2. GET ELEMENTS (Matches: fetch('/NonSelectable/GetElements'))
    [HttpGet]
    public async Task<IActionResult> GetElements(int subAreaId)
    {
        var elements = await context.NonSelectable
            .Where(e => e.SubAreaId == subAreaId)
            .ToListAsync();
        return Ok(elements);
    }

    // DTO for Updating
    public class NonSelectableUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Height {get; set; }
        public decimal Width {get; set; }
    }

    // 3. UPDATE SINGLE SHAPE (Renamed to match: fetch('/NonSelectable/UpdateElement'))
    [HttpPost]
    [ValidateAntiForgeryToken] 
    public async Task<IActionResult> UpdateElement([FromBody] NonSelectableUpdateDto request)
    {
        if (request == null || request.Id <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid shape data." });
        }

        var shape = await context.NonSelectable.FindAsync(request.Id);
        if (shape == null)
        {
            return NotFound(new { success = false, message = "Shape not found." });
        }

        shape.Name = request.Name;
        shape.X = request.X;
        shape.Y = request.Y;
        shape.Height = request.Height;
        shape.Width = request.Width;

        try
        {
            context.Update(shape);
            await context.SaveChangesAsync();
            
            return Ok(new { success = true });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new { success = false, message = "Database error occurred while updating the shape." });
        }
    }

    // DTO for Deleting
    public class DeleteMultipleElementsRequest
    {
        public List<string> Names { get; set; }
        public int SubAreaId { get; set; }
    }

    // 4. DELETE MULTIPLE SHAPES (Added to match: fetch('/NonSelectable/DeleteElements'))
    [HttpPost]
    public async Task<IActionResult> DeleteElements([FromBody] DeleteMultipleElementsRequest request)
    {
        if (request == null || request.Names == null || !request.Names.Any())
        {
            return BadRequest(new { success = false, message = "No shape names provided." });
        }

        var shapesToRemove = await context.NonSelectable
            .Where(s => request.Names.Contains(s.Name) && s.SubAreaId == request.SubAreaId)
            .ToListAsync();

        if (!shapesToRemove.Any())
            return Json(new { success = false, message = "No matching shapes found to delete." });

        context.NonSelectable.RemoveRange(shapesToRemove);
        await context.SaveChangesAsync();

        return Json(new { success = true });
    }
}

