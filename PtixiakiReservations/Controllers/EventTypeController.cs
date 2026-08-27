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
    /// Manages administrative operations for Event Types (Categories).
    /// Handles database records as well as physical disk I/O for category imagery.
    /// </summary>
    [Authorize(Roles = "Admin")] // Restrict the entire controller to administrative personnel
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
        /// Retrieves a paginated and searchable directory of all registered event types.
        /// Execution is deferred so the database handles filtering and counting efficiently.
        /// </summary>
        /// <param name="searchQuery">Optional text to filter event types by name.</param>
        /// <param name="pageNumber">The current page index representing the data offset.</param>
        [HttpGet]
        public async Task<IActionResult> ManageEventType(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            // 1. Initialize deferred query execution against the EventType table
            var query = _context.EventType.AsQueryable();

            // 2. Apply search filters dynamically if a search string is provided
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                
                // Using EF Core's built-in translation to execute the LIKE query in SQL
                query = query.Where(et => et.Name.ToLower().Contains(normalizedQuery));
            }

            // 3. Calculate pagination metadata boundaries
            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            // Enforce safe boundary limits to prevent out-of-bounds page requests
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            // 4. Extract the exact subset of records required for the current view
            var eventTypes = await query
                .OrderBy(et => et.Name) // Deterministic sorting required for SQL Skip/Take
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 5. Inject pagination metadata into the ViewBag for frontend UI rendering
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(eventTypes);
        }

        /// <summary>
        /// Renders the standalone event type creation form.
        /// </summary>
        [HttpGet]
        public IActionResult CreateEventType()
        {
            return View();
        }

        /// <summary>
        /// Processes the creation of a new event type, including physical disk I/O for uploaded imagery.
        /// </summary>
        /// <param name="ETName">The string identifier for the new category.</param>
        /// <param name="imageFile">The multipart form file representing the category's display image.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEventType(string ETName, IFormFile? imageFile)
        {
            // Guard clause enforcing required naming
            if (string.IsNullOrWhiteSpace(ETName))
            {
                ModelState.AddModelError("ETName", "Event Type Name is required.");
                return View();
            }

            string imagePath = null;
            
            // Process the uploaded image file if provided by the user
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    // Map the virtual path to the physical server directory
                    string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                    
                    // Ensure the directory exists before attempting to write to it
                    if (!Directory.Exists(uploadsFolder)) 
                        Directory.CreateDirectory(uploadsFolder);

                    // Generate a cryptographically unique filename to prevent overwriting existing assets
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    // Stream the file asynchronously to the disk
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(fileStream);
                    }

                    // Store the relative virtual path in the database for frontend rendering
                    imagePath = "/images/eventTypes/" + uniqueFileName;
                }
                catch (Exception ex)
                {
                    // Log disk I/O failures to prevent silent application crashes
                    _logger.LogError(ex, "Error saving event type image to disk.");
                    return BadRequest(new { success = false, message = "Error saving image." });
                }
            }
            
            // Construct and track the new entity
            _context.EventType.Add(new EventType { Name = ETName.Trim(), ImagePath = imagePath });
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ManageEventType));
        }

        /// <summary>
        /// Permanently removes an event type from the platform.
        /// Note: In a strict production environment, you might also want to delete the physical image file here.
        /// </summary>
        /// <param name="id">The unique integer identifier of the target event type.</param>
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
        /// Retrieves a specific event type and renders the modification interface.
        /// </summary>
        /// <param name="ETId">The unique integer identifier of the target event type.</param>
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
        /// Commits modifications to an existing event type, including replacing its display image.
        /// </summary>
        /// <param name="ETId">The primary key of the event type being edited.</param>
        /// <param name="ETName">The new category name being applied.</param>
        /// <param name="imageFile">An optional new image file replacing the old asset.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEventType(int ETId, string ETName, IFormFile? imageFile)
        {
            var et = await _context.EventType.FindAsync(ETId);

            if (et != null)
            {
                // Validate Name Input
                if (string.IsNullOrWhiteSpace(ETName))
                {
                    ModelState.AddModelError("ETName", "Event Type Name is required.");
                    return View(et); // Return the model back to the view so data isn't lost
                }
                else
                {
                    et.Name = ETName.Trim();
                }
                
                // Process image replacement if a new file was uploaded
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

                        // Update the entity's path to point to the newly uploaded image
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
        /// Public API Endpoint: Exposes a lightweight JSON payload of all categories.
        /// Used dynamically by the frontend (e.g., JavaScript Dropdowns or Booking Interfaces) 
        /// to render category options without requiring administrative authorization.
        /// </summary>
        [AllowAnonymous] // Ensures standard users and guests can query this list
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.EventType
                // Projecting to an anonymous type to strip out unnecessary database metadata and minimize payload size
                .Select(c => new { id = c.Id, name = c.Name, imagePath = c.ImagePath })
                .ToListAsync();

            return Json(categories);
        }
    }
}