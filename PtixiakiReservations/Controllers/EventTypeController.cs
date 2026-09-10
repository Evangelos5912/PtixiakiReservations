using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;

namespace PtixiakiReservations.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EventTypeController : Controller
    {       
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<EventTypeController> _logger;

        public EventTypeController(
            IWebHostEnvironment environment, 
            ILogger<EventTypeController> logger, 
            ApplicationDbContext context)
        {
            _environment = environment;
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> ManageEventType(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 
            var query = _context.EventType.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(et => et.Name.ToLower().Contains(normalizedQuery));
            }

            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var eventTypes = await query
                .OrderBy(et => et.Name) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(eventTypes);
        }

        [HttpGet]
        public IActionResult CreateEventType()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEventType(string ETName, IFormFile? imageFile)
        {
            if (string.IsNullOrWhiteSpace(ETName))
            {
                ModelState.AddModelError("ETName", "Event Type Name is required.");
                return View();
            }

            string imagePath = null;
            
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(fileStream);
                    }

                    imagePath = "/images/eventTypes/" + uniqueFileName;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving event type image to disk.");
                    return BadRequest(new { success = false, message = "Error saving image." });
                }
            }
            
            _context.EventType.Add(new EventType { Name = ETName.Trim(), ImagePath = imagePath });
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ManageEventType));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEventType(int id)
        {
            var et = await _context.EventType.FindAsync(id);
            if (et == null) return RedirectToAction(nameof(ManageEventType));

            try
            {
                var masterEventIds = await _context.Event.Where(e => e.EventTypeId == id).Select(e => e.Id).ToListAsync();
                var childEventIds = await _context.Event.Where(e => e.ParentEventId.HasValue && masterEventIds.Contains(e.ParentEventId.Value)).Select(e => e.Id).ToListAsync();
                var allEventIds = masterEventIds.Concat(childEventIds).Distinct().ToList();

                var eventImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync();
                var galleryImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync();

                await _context.VenueCategory.Where(vc => vc.CategoryId == id).ExecuteDeleteAsync();

                if (allEventIds.Any())
                {
                    // FIX: Detach events from user wishlists first to prevent FK crashes
                    await _context.Set<Wishlist>().Where(w => allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();

                    await _context.Reservation.Where(r => allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();
                    await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();

                    if (childEventIds.Any())
                        await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    
                    if (masterEventIds.Any())
                        await _context.Event.Where(e => masterEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                }

                var allPathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
                foreach (var path in allPathsToDelete)
                {
                    string fullPath = Path.Combine(_environment.WebRootPath, path.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }

                if (!string.IsNullOrEmpty(et.ImagePath))
                {
                    string fullPath = Path.Combine(_environment.WebRootPath, et.ImagePath.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }

                _context.EventType.Remove(et);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute memory-safe cascading deletion for Event Type ID {Id}", id);
                TempData["ErrorMessage"] = "Could not delete category due to database constraints.";
            }
            
            return RedirectToAction(nameof(ManageEventType));
        }

        [HttpGet]
        public async Task<IActionResult> EditEventType(int ETId)
        {
            var et = await _context.EventType.FindAsync(ETId);
            if (et == null) return NotFound();
            return View(et);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEventType(int ETId, string ETName, IFormFile? imageFile)
        {
            var et = await _context.EventType.FindAsync(ETId);

            if (et != null)
            {
                if (string.IsNullOrWhiteSpace(ETName))
                {
                    ModelState.AddModelError("ETName", "Event Type Name is required.");
                    return View(et); 
                }
                et.Name = ETName.Trim();
                
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        if (!string.IsNullOrEmpty(et.ImagePath))
                        {
                            string oldPath = Path.Combine(_environment.WebRootPath, et.ImagePath.TrimStart('/', '\\'));
                            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                        }

                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        et.ImagePath = "/images/eventTypes/" + uniqueFileName;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error saving event type image to disk.");
                        return BadRequest(new { success = false, message = "Error saving image." });
                    }
                }
            
                _context.Update(et);
                await _context.SaveChangesAsync();
                
                return RedirectToAction(nameof(ManageEventType));
            }
            return NoContent();
        }

        [AllowAnonymous] 
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.EventType
                .Select(c => new { id = c.Id, name = c.Name, imagePath = c.ImagePath })
                .ToListAsync();

            return Json(categories);
        }
    }
}