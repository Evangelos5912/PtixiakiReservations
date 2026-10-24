using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

namespace PtixiakiReservations.Controllers;

// Handles CRUD operations for non-selectable layout elements (shapes, tables, stages)
public class NonSelectableController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    : Controller
{
    // Replaces all existing non-selectable elements for a layout with a new set
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveElements(int layoutId, [FromBody] List<NonSelectable> elements)
    {
        var existing = await context.NonSelectable
            .Where(e => e.LayoutId == layoutId)
            .ToListAsync();

        context.NonSelectable.RemoveRange(existing);
        
        foreach (var el in elements)
        {
            el.LayoutId = layoutId;
            context.NonSelectable.Add(el);
        }

        await context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // Retrieves all non-selectable elements associated with a specific layout
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetElements(int layoutId)
    {
        var elements = await context.NonSelectable
            .Where(e => e.LayoutId == layoutId)
            .ToListAsync();
        return Ok(elements);
    }

    // DTO for updating non-selectable element dimensions and positions
    public class NonSelectableUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Height {get; set; }
        public decimal Width {get; set; }
    }

    // Updates the position and dimensions of a specific non-selectable element
    [HttpPost]
    [ValidateAntiForgeryToken] 
    [Authorize(Roles = "Admin")]
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

    // DTO for bulk deletion of non-selectable elements by name
    public class DeleteMultipleElementsRequest
    {
        public List<string> Names { get; set; }
        public int LayoutId { get; set; }
    }

    // Bulk deletes multiple non-selectable elements from a layout
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteElements([FromBody] DeleteMultipleElementsRequest request)
    {
        if (request == null || request.Names == null || !request.Names.Any())
        {
            return BadRequest(new { success = false, message = "No shape names provided." });
        }

        var shapesToRemove = await context.NonSelectable
            .Where(s => request.Names.Contains(s.Name) && s.LayoutId == request.LayoutId)
            .ToListAsync();

        if (!shapesToRemove.Any())
            return Json(new { success = false, message = "No matching shapes found to delete." });

        context.NonSelectable.RemoveRange(shapesToRemove);
        await context.SaveChangesAsync();

        return Json(new { success = true });
    }
}