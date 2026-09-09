using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages administrative operations for Event Types (Categories), including image asset management.
    /// </summary>
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

        /// <summary>
        /// Retrieves a paginated and searchable list of event types.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ManageEventType(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            var query = _context.EventType.AsQueryable();

            // Apply search filters.
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(et => et.Name.ToLower().Contains(normalizedQuery));
            }

            // Calculate pagination metadata.
            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            // Retrieve paginated records.
            var eventTypes = await query
                .OrderBy(et => et.Name) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Populate view context.
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(eventTypes);
        }

        /// <summary>
        /// Renders the event type creation interface.
        /// </summary>
        [HttpGet]
        public IActionResult CreateEventType()
        {
            return View();
        }

        /// <summary>
        /// Validates and creates a new event type, processing any associated image uploads.
        /// </summary>
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
            
            // Process image upload.
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                    
                    if (!Directory.Exists(uploadsFolder)) 
                        Directory.CreateDirectory(uploadsFolder);

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

        /// <summary>
        /// Permanently removes an event type from the database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEventType(int id)
        {
            var et = await _context.EventType.FindAsync(id);

            if (et != null)
            {
                _context.EventType.Remove(et);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(ManageEventType));
        }

        /// <summary>
        /// Retrieves a specific event type and renders the edit interface.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> EditEventType(int ETId)
        {
            var et = await _context.EventType.FindAsync(ETId);

            if (et == null)
            {
                return NotFound();
            }

            return View(et);
        }

        /// <summary>
        /// Commits modifications to an existing event type, including image replacement.
        /// </summary>
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
                else
                {
                    et.Name = ETName.Trim();
                }
                
                // Process image replacement.
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                        if (!Directory.Exists(uploadsFolder)) 
                            Directory.CreateDirectory(uploadsFolder);

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
            else
            {
                return NoContent();
            }
        }

        /// <summary>
        /// Exposes a lightweight JSON payload of all categories for frontend client use.
        /// </summary>
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