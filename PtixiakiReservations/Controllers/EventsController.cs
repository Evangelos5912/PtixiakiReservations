using Microsoft.AspNetCore.Authorization;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;
using PtixiakiReservations.Services;
using System.Text;
using System.Text.Json; 
using Microsoft.AspNetCore.Hosting; 
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;

namespace PtixiakiReservations.Controllers;

/// <summary>
/// Manages event lifecycle operations, hierarchical sub-events, 
/// Elasticsearch indexing, and media processing.
/// </summary>
public class EventsController(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IElasticSearch elasticSearchService,
    ILogger<EventsController> logger,
    IWebHostEnvironment environment)
    : Controller
{
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 12)
    {
        var query = context.Event.AsQueryable();
        int totalCount = await query.CountAsync();

        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize);
        return View();
    }

    /// <summary>
    /// Dynamically transcodes images to WebP format.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCompressedImage(string path, int width = 800, int quality = 75)
    {
        if (string.IsNullOrWhiteSpace(path)) return NotFound();

        string physicalPath = Path.Combine(environment.WebRootPath, path.TrimStart('/', '\\'));
        if (!System.IO.File.Exists(physicalPath)) return NotFound();

        try
        {
            using var image = await Image.LoadAsync(physicalPath);
            
            if (image.Width > width)
            {
                int newHeight = (int)((double)image.Height / image.Width * width);
                image.Mutate(x => x.Resize(width, newHeight));
            }

            var memoryStream = new MemoryStream();
            var encoder = new WebpEncoder { Quality = quality };
            await image.SaveAsync(memoryStream, encoder);
            memoryStream.Position = 0;

            return File(memoryStream, "image/webp");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to transcode image stream. Falling back to original asset.");
            return PhysicalFile(physicalPath, "application/octet-stream"); 
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetTodayEvents(string city, int page = 1, int pageSize = 12)
    {
        var today = DateTime.Today;
        var eventsQuery = context.Event
            .Include(e => e.Venue)
            .ThenInclude(v => v.City)
            .Where(e => e.StartDateTime.Date == today)
            .OrderBy(e => e.StartDateTime);

        if (!string.IsNullOrWhiteSpace(city))
        {
            eventsQuery = (IOrderedQueryable<Event>)eventsQuery
                .Where(e => e.Venue.City.Name.ToLower() == city.ToLower());
        }

        var totalCount = await eventsQuery.CountAsync();
        
        var events = await eventsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=600") 
                    : null
            })
            .ToListAsync();

        return Json(new { events, totalCount, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetUpcomingEvents(string city, int page = 1, int pageSize = 12)
    {
        var today = DateTime.Today;
        var eventsQuery = context.Event
            .Where(e => e.StartDateTime.Date > today)
            .OrderBy(e => e.StartDateTime);

        if (!string.IsNullOrWhiteSpace(city))
        {
            eventsQuery = (IOrderedQueryable<Event>)eventsQuery
                .Where(e => e.Venue.City.Name.ToLower() == city.ToLower());
        }

        var totalCount = await eventsQuery.CountAsync();
        
        var events = await eventsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=600") 
                    : null
            })
            .ToListAsync();

        return Json(new { events, totalCount, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPastEvents(string city, int page = 1, int pageSize = 12)
    {
        var today = DateTime.Today;
        var eventsQuery = context.Event
            .Where(e => e.StartDateTime.Date < today)
            .OrderByDescending(e => e.StartDateTime);

        if (!string.IsNullOrWhiteSpace(city))
        {
            eventsQuery = (IOrderedQueryable<Event>)eventsQuery
                .Where(e => e.Venue.City.Name.ToLower() == city.ToLower());
        }

        var totalCount = await eventsQuery.CountAsync();
        
        var events = await eventsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=600") 
                    : null
            })
            .ToListAsync();

        return Json(new { events, totalCount, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllEvents(string city, int page = 1, int pageSize = 12)
    {
        var eventsQuery = context.Event.OrderBy(e => e.StartDateTime);

        if (!string.IsNullOrWhiteSpace(city))
        {
            eventsQuery = (IOrderedQueryable<Event>)eventsQuery
                .Where(e => e.Venue.City.Name.ToLower() == city.ToLower());
        }

        var totalCount = await eventsQuery.CountAsync();
        
        var events = await eventsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=600") 
                    : null
            })
            .ToListAsync();

        return Json(new { events, totalCount, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
    }

    [AllowAnonymous]
    public string GetEventTimeClass(DateTime eventDate)
    {
        DateTime today = DateTime.Today;
        if (eventDate.Date == today) return "event-today";
        else if (eventDate.Date > today) return "event-upcoming";
        else return "event-past";
    }

    [AllowAnonymous]
    public async Task<IActionResult> EventsForToday(int? category, string city, string searchTerm, int page = 1, int pageSize = 12)
    {
        var today = DateTime.Today;
        var eventsQuery = context.Event
            .Include(e => e.Venue)
            .ThenInclude(v => v.City)
            .Include(e => e.EventType) 
            .Include(e => e.ChildEvents)
            .ThenInclude(c => c.Venue)      
            .ThenInclude(v => v.City)
            .Where(e => e.ParentEventId == null && e.EndTime >= today); 

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            eventsQuery = eventsQuery.Where(e => 
                e.Name.ToLower().Contains(term) || 
                (e.Venue != null && e.Venue.Name.ToLower().Contains(term)) || 
                (e.Venue != null && e.Venue.City != null && e.Venue.City.Name.ToLower().Contains(term))
            );
            ViewBag.SearchTerm = searchTerm; 
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            eventsQuery = eventsQuery.Where(e => e.Venue.City.Name.ToLower() == city.ToLower());
        }

        if (category.HasValue && category.Value > 0)
        {
            eventsQuery = eventsQuery.Where(e => e.EventTypeId == category.Value);
        }

        eventsQuery = eventsQuery.OrderBy(e => e.StartDateTime);
        int totalMasterEvents = await eventsQuery.CountAsync();

        ViewBag.TotalMasterEvents = totalMasterEvents; 
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalMasterEvents / pageSize);
        ViewBag.CurrentPage = page;
        
        var eventsList = await eventsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var masterIds = eventsList.Select(e => e.Id).ToList();

        var childCounts = await context.Event
            .Where(e => e.ParentEventId != null && masterIds.Contains(e.ParentEventId.Value))
            .GroupBy(e => e.ParentEventId.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParentId, x => x.Count);

        ViewBag.ChildCounts = childCounts;

        return View(eventsList);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var eventDetails = await context.Event
            .Include(e => e.Organizer)
            .Include(e => e.EventType)
            .Include(e => e.GalleryImages) 
            .Include(e => e.Venue)
                .ThenInclude(v => v.City)
            .Include(e => e.ParentEvent)
                .ThenInclude(p => p.GalleryImages)
            .Include(e => e.ChildEvents) 
                .ThenInclude(c => c.Layout)
            .Include(e => e.ChildEvents) 
                .ThenInclude(c => c.Venue)
            .AsSplitQuery() 
            .FirstOrDefaultAsync(m => m.Id == id);

        if (eventDetails == null) return NotFound();

        return View(eventDetails);
    }

    [HttpGet]
    public async Task<IActionResult> GetEventGallery(int eventId)
    {
        var targetEvent = await context.Event
            .Include(e => e.GalleryImages)
            .Include(e => e.ParentEvent)
                .ThenInclude(p => p.GalleryImages)
            .FirstOrDefaultAsync(e => e.Id == eventId);

        if (targetEvent == null) return Json(new { success = false, message = "Event not found" });

        var paths = new List<string>();
        var galleryToUse = (targetEvent.GalleryImages != null && targetEvent.GalleryImages.Any()) 
            ? targetEvent.GalleryImages 
            : targetEvent.ParentEvent?.GalleryImages;

        if (galleryToUse != null && galleryToUse.Any())
        {
            paths.AddRange(galleryToUse.Select(g => 
                g.ImagePath.EndsWith(".webp") 
                    ? g.ImagePath 
                    : $"/Events/GetCompressedImage?path={g.ImagePath}&width=1000"));
        }

        return Json(new { success = true, images = paths });
    }

    [Authorize]
    public async Task<JsonResult> GetEvents()
    {
        var userId = userManager.GetUserId(User);
        var events = await context.Event
            .Include(e => e.Venue)
            .Where(e => e.Venue.UserId == userId)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                eventType = e.EventType != null ? e.EventType.Name : "Default",
                venueName = e.Venue.Name
            })
            .ToListAsync();
            
        return Json(events);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetEvents2(int? venueId)
    {
        var events = await context.Event
            .Where(e => e.VenueId == venueId)
            .Include(e => e.EventType) 
            .ToListAsync();

        var result = events.Select(e => new {
            id = e.Id,
            name = e.Name,
            startDateTime = e.StartDateTime,
            endTime = e.EndTime,
            eventType = e.EventType != null ? e.EventType.Name : "Default" 
        });

        return Json(result); 
    }

    [AllowAnonymous]
    public JsonResult GetEventTypes()
    {
        var eventsTypes = context.EventType.ToList();
        return new JsonResult(eventsTypes);
    }

    [Authorize]
    public async Task<IActionResult> VenueEvents(int venueId)
    {
        var venue = await context.Venue.FirstOrDefaultAsync(v => v.Id == venueId);
        if (venue is null) return NotFound();
        return View(venue);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> CreateEvent()
    {
        try
        {
            var userId = userManager.GetUserId(User);
            var venues = await context.Venue
                .Select(v => new SelectListItem { Value = v.Id.ToString(), Text = v.Name })
                .ToListAsync();

            if (venues.Count == 0)
            {
                TempData["ErrorMessage"] = "You need to create a venue before you can create an event.";
                return RedirectToAction("Create", "Venue");
            }

            ViewBag.VenueList = venues;
            var eventTypes = await context.EventType.ToListAsync();
            
            if (eventTypes.Count == 0)
            {
                TempData["ErrorMessage"] = "No event types are available. Please contact an administrator.";
                return RedirectToAction("Index");
            }

            ViewBag.EventTypeList = new SelectList(eventTypes, "Id", "Name");
            return View(new Event());
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred while preparing the form. Please try again.";
            return RedirectToAction("Index");
        }
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEvent(
        Event newEvent,
        IFormFile? imageFile,
        List<IFormFile>? galleryFiles,
        string IsMultiDay = null,
        string IsStandalone = null,
        string StartTime = null,
        string MultiEndTime = null,
        string SpecificDatesJson = null)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(new { success = false, message = "Invalid form data.", errors });
        }

        bool isMultiDay = IsMultiDay == "on" || IsMultiDay == "true"; 
        bool isStandalone = IsStandalone == "true" || IsStandalone == "on" || IsStandalone == "true,false" || isMultiDay;
        bool isChild = newEvent.ParentEventId.HasValue;

        // Validates venue and layout dependencies based on the designated event mode.
        if (isChild || isStandalone)
        {
            if (newEvent.VenueId == null || newEvent.VenueId == 0 || newEvent.LayoutId == null || newEvent.LayoutId == 0)
            {
                return BadRequest(new { 
                    success = false, 
                    message = "Security Check Failed: A Venue and Layout are strictly required for this event mode." 
                });
            }
        }
        else
        {
            newEvent.VenueId = null;
            newEvent.LayoutId = null;
        }

        PtixiakiReservations.Models.Venue venue = null;
        if (newEvent.VenueId.HasValue)
        {
            venue = await context.Venue.FirstOrDefaultAsync(v => v.Id == newEvent.VenueId);
            if (venue == null) return BadRequest(new { success = false, message = "Venue does not exist." });
        }

        var userId = userManager.GetUserId(User);
        newEvent.OrganizerId = userId;

        // Processes and stores the primary image asset.
        if (imageFile != null && imageFile.Length > 0)
        {
            try
            {
                string uploadsFolder = Path.Combine(environment.WebRootPath, "images", "events");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                string uniqueFileName = Guid.NewGuid().ToString() + ".webp";
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var image = await Image.LoadAsync(imageFile.OpenReadStream()))
                {
                    if (image.Width > 1920)
                    {
                        int newHeight = (int)((double)image.Height / image.Width * 1920);
                        image.Mutate(x => x.Resize(1920, newHeight));
                    }

                    var encoder = new WebpEncoder { Quality = 80 };
                    await image.SaveAsync(filePath, encoder);
                }

                newEvent.ImagePath = "/images/events/" + uniqueFileName;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing event image data.");
                return BadRequest(new { success = false, message = "Error processing image data." });
            }
        }

        // Processes and stores gallery image assets.
        if (galleryFiles != null && galleryFiles.Count > 0)
        {
            string galleryFolder = Path.Combine(environment.WebRootPath, "images", "events", "gallery");
            if (!Directory.Exists(galleryFolder)) Directory.CreateDirectory(galleryFolder);

            newEvent.GalleryImages ??= new List<EventImage>();

            foreach (var file in galleryFiles)
            {
                if (file.Length > 0)
                {
                    string uniqueFileName = Guid.NewGuid().ToString() + ".webp";
                    string filePath = Path.Combine(galleryFolder, uniqueFileName);

                    using (var image = await Image.LoadAsync(file.OpenReadStream()))
                    {
                        if (image.Width > 1920)
                        {
                            int newHeight = (int)((double)image.Height / image.Width * 1920);
                            image.Mutate(x => x.Resize(1920, newHeight));
                        }

                        var encoder = new WebpEncoder { Quality = 80 };
                        await image.SaveAsync(filePath, encoder);
                    }

                    newEvent.GalleryImages.Add(new EventImage { ImagePath = "/images/events/gallery/" + uniqueFileName });
                }
            }
        }
       
        try
        {
            Event fatherEvent = null;

            if (isMultiDay && !string.IsNullOrEmpty(SpecificDatesJson)
                && !string.IsNullOrEmpty(StartTime) && !string.IsNullOrEmpty(MultiEndTime))
            {
                var selectedDates = JsonSerializer.Deserialize<List<string>>(SpecificDatesJson);
                TimeSpan startTimeSpan = DateTime.TryParse(StartTime, out DateTime pst) ? pst.TimeOfDay : TimeSpan.Parse(StartTime);
                TimeSpan endTimeSpan = DateTime.TryParse(MultiEndTime, out DateTime pet) ? pet.TimeOfDay : TimeSpan.Parse(MultiEndTime);

                if (!newEvent.ParentEventId.HasValue)
                {
                    if (newEvent.StartDateTime == DateTime.MinValue && selectedDates.Any())
                        newEvent.StartDateTime = DateTime.Parse(selectedDates.First()).Date.Add(startTimeSpan);
                    if (newEvent.EndTime == DateTime.MinValue && selectedDates.Any())
                        newEvent.EndTime = DateTime.Parse(selectedDates.Last()).Date.Add(endTimeSpan);

                    context.Add(newEvent);
                    await context.SaveChangesAsync(); 
                    fatherEvent = newEvent;
                }

                var count = 1;
                foreach (var dateString in selectedDates)
                {
                    if (DateTime.TryParse(dateString, out DateTime date))
                    {
                        var eventForDay = new Event
                        {
                            Name = newEvent.Name + " Day " + count,
                            Description = newEvent.Description,
                            VenueId = newEvent.VenueId, 
                            EventTypeId = newEvent.EventTypeId,
                            LayoutId = newEvent.LayoutId, 
                            StartDateTime = date.Date.Add(startTimeSpan),
                            EndTime = date.Date.Add(endTimeSpan),
                            ImagePath = newEvent.ImagePath,
                            OrganizerId = userId,
                            ParentEventId = newEvent.ParentEventId ?? fatherEvent?.Id
                        };

                        context.Add(eventForDay);
                        count++;
                    }
                }
            }
            else
            {
                if (newEvent.StartDateTime == DateTime.MinValue) newEvent.StartDateTime = DateTime.Now;
                if (newEvent.EndTime == DateTime.MinValue) newEvent.EndTime = newEvent.StartDateTime.AddHours(2);
                context.Add(newEvent);
            }

            await context.SaveChangesAsync();

            int? returnedEventId = newEvent.ParentEventId ?? (isMultiDay && fatherEvent != null ? fatherEvent.Id : newEvent.Id);

            return Json(new { 
                success = true, 
                eventId = returnedEventId, 
                eventName = newEvent.Name,
                venueId = newEvent.VenueId,
                venueName = venue?.Name, 
                eventTypeId = newEvent.EventTypeId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating event data.");
            return BadRequest(new { success = false, message = "An error occurred while creating the event." });
        }
    }

    /// <summary>
    /// Retrieves the designated event and initializes the modification interface.
    /// Incorporates hierarchical parent data and existing media collections for frontend rendering.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        // 1. CRITICAL: We MUST include the City, ParentEvent, and GalleryImages
        var eventToEdit = await context.Event
            .Include(e => e.Venue)
                .ThenInclude(v => v.City) 
            .Include(e => e.EventType)
            .Include(e => e.Layout)
            .Include(e => e.GalleryImages) 
            .Include(e => e.ParentEvent)
                .ThenInclude(p => p.GalleryImages) 
            .AsSplitQuery() 
            .FirstOrDefaultAsync(e => e.Id == id);

        if (eventToEdit == null) return NotFound();

        var currentUserId = userManager.GetUserId(User);
        bool isOwner = eventToEdit.OrganizerId == currentUserId;
        bool isAdmin = User.IsInRole("Admin");

        if (!isAdmin && !isOwner) return Forbid(); 

        // 2. CRITICAL: This MUST be named VenueList (not Venue) to match the frontend
        ViewBag.VenueList = await context.Venue
            .Include(v => v.City)
            .Where(v => v.UserId == currentUserId)
            .Select(v => new SelectListItem { 
                Value = v.Id.ToString(), 
                Text = v.City != null ? $"{v.Name}, {v.City.Name}" : v.Name 
            })
            .ToListAsync();

        ViewBag.EventTypeList = new SelectList(await context.EventType.ToListAsync(), "Id", "Name", eventToEdit.EventTypeId);

        ViewBag.LayoutList = await context.Layout
            .Where(sa => sa.VenueId == eventToEdit.VenueId)
            .Select(sa => new SelectListItem { Value = sa.Id.ToString(), Text = sa.AreaName })
            .ToListAsync();

        return View(eventToEdit);
    }

    /// <summary>
    /// Processes modifications to an existing event, handling relational data updates 
    /// and executing media transcoding pipelines for incoming image assets.
    /// </summary>
    /// <param name="id">The unique identifier of the event being modified.</param>
    /// <param name="updatedEvent">The bound data model containing updated properties.</param>
    /// <param name="imageFile">The optional primary poster image upload.</param>
    /// <param name="galleryFiles">The optional collection of supplemental gallery images.</param>
    /// <returns>A redirect to the venue events index upon success, or the validation-populated view upon failure.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize] 
    public async Task<IActionResult> Edit(int id, Event updatedEvent, IFormFile? imageFile, IFormFileCollection? galleryFiles)
    {
        if (id != updatedEvent.Id) return NotFound();

        var currentUserId = userManager.GetUserId(User);

        ViewBag.VenueList = await context.Venue
            .Where(v => v.UserId == currentUserId)
            .Select(v => new SelectListItem { Value = v.Id.ToString(), Text = v.Name })
            .ToListAsync();
        ViewBag.EventTypeList = new SelectList(await context.EventType.ToListAsync(), "Id", "Name", updatedEvent.EventTypeId);
        ViewBag.LayoutList = await context.Layout
            .Where(sa => sa.VenueId == updatedEvent.VenueId)
            .Select(sa => new SelectListItem { Value = sa.Id.ToString(), Text = sa.AreaName })
            .ToListAsync();

        if (ModelState.IsValid)
        {
            try
            {
                var originalEvent = await context.Event
                    .Include(e => e.GalleryImages)
                    .FirstOrDefaultAsync(e => e.Id == id);
                    
                if (originalEvent == null) return NotFound();

                if (!User.IsInRole("Admin") && originalEvent.OrganizerId != currentUserId) return Forbid();

                if (imageFile != null && imageFile.Length > 0)
                {
                    if (!string.IsNullOrEmpty(originalEvent.ImagePath))
                    {
                        string oldFilePath = Path.Combine(environment.WebRootPath, originalEvent.ImagePath.TrimStart('/'));
                        if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);
                    }

                    try
                    {
                        string uploadsFolder = Path.Combine(environment.WebRootPath, "images", "events");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        string uniqueFileName = Guid.NewGuid().ToString() + ".webp";
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var image = await Image.LoadAsync(imageFile.OpenReadStream()))
                        {
                            if (image.Width > 1920)
                            {
                                int newHeight = (int)((double)image.Height / image.Width * 1920);
                                image.Mutate(x => x.Resize(1920, newHeight));
                            }

                            var encoder = new WebpEncoder { Quality = 80 };
                            await image.SaveAsync(filePath, encoder);
                        }

                        originalEvent.ImagePath = "/images/events/" + uniqueFileName;
                    }
                    catch (Exception)
                    {
                        ModelState.AddModelError("", "Error processing primary image file.");
                        
                        updatedEvent.ImagePath = originalEvent.ImagePath;
                        updatedEvent.GalleryImages = originalEvent.GalleryImages;
                        updatedEvent.ParentEventId = originalEvent.ParentEventId;
                        return View(updatedEvent);
                    }
                }

                if (galleryFiles != null && galleryFiles.Count > 0)
                {
                    string galleryFolder = Path.Combine(environment.WebRootPath, "images", "events", "gallery");
                    if (!Directory.Exists(galleryFolder)) Directory.CreateDirectory(galleryFolder);

                    originalEvent.GalleryImages ??= new List<EventImage>();

                    foreach (var file in galleryFiles)
                    {
                        if (file.Length > 0)
                        {
                            string uniqueGalleryName = Guid.NewGuid().ToString() + ".webp";
                            string galleryPath = Path.Combine(galleryFolder, uniqueGalleryName);

                            using (var image = await Image.LoadAsync(file.OpenReadStream()))
                            {
                                if (image.Width > 1920)
                                {
                                    int newHeight = (int)((double)image.Height / image.Width * 1920);
                                    image.Mutate(x => x.Resize(1920, newHeight));
                                }

                                var encoder = new WebpEncoder { Quality = 80 };
                                await image.SaveAsync(galleryPath, encoder);
                            }

                            originalEvent.GalleryImages.Add(new EventImage 
                            {
                                ImagePath = "/images/events/gallery/" + uniqueGalleryName
                            });
                        }
                    }
                }

                originalEvent.Name = updatedEvent.Name;
                originalEvent.StartDateTime = updatedEvent.StartDateTime;
                originalEvent.EndTime = updatedEvent.EndTime;
                originalEvent.EventTypeId = updatedEvent.EventTypeId;
                originalEvent.VenueId = updatedEvent.VenueId;
                originalEvent.LayoutId = updatedEvent.LayoutId;

                if (originalEvent.ParentEventId == null)
                {
                    originalEvent.Description = updatedEvent.Description;
                }

                await context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Event configuration successfully updated.";
                return RedirectToAction(nameof(VenueEvents), new { venueId = updatedEvent.VenueId });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EventExists(updatedEvent.Id)) return NotFound();
                else ModelState.AddModelError("", "The event was modified by another transaction. Please retry.");
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "An error occurred while updating the event schema.");
            }
        }

        var fallbackEvent = await context.Event.Include(e => e.GalleryImages).AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (fallbackEvent != null)
        {
            updatedEvent.ImagePath = fallbackEvent.ImagePath;
            updatedEvent.GalleryImages = fallbackEvent.GalleryImages;
            updatedEvent.ParentEventId = fallbackEvent.ParentEventId;
        }

        return View(updatedEvent);
    }


    [Authorize]
    public bool CorrectDay(JsonEventModel ev, int i, int everyNum)
    {
        bool correctDay = false;
        if (ev.Repeat.M == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Monday") correctDay = true;
        else if (ev.Repeat.Tu == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Tuesday") correctDay = true;
        else if (ev.Repeat.W == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Wednesday") correctDay = true;
        else if (ev.Repeat.Th == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Thursday") correctDay = true;
        else if (ev.Repeat.F == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Friday") correctDay = true;
        else if (ev.Repeat.Sa == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Saturday") correctDay = true;
        else if (ev.Repeat.Su == true && ev.StartDateTime.AddDays(i + everyNum * 7).DayOfWeek.ToString() == "Sunday") correctDay = true;

        return correctDay;
    }

    [Authorize]
    [HttpDelete]
  
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var ev = await context.Event.FirstOrDefaultAsync(e => e.Id == id);
            
        if (ev == null) return NotFound();

        var userId = userManager.GetUserId(User);
        if (ev.OrganizerId != userId && !User.IsInRole("Admin")) return Unauthorized();


        if (ev.ParentEventId == null)
        {
            await context.Event
                .Where(e => e.ParentEventId == id)
                .ExecuteDeleteAsync();
        }


        await context.Event
            .Where(e => e.Id == id)
            .ExecuteDeleteAsync();


        return Ok();
    }
    private bool EventExists(int id) => context.Event.Any(e => e.Id == id);

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> IndexEventsToElastic()
    {
        var events = new List<Event>
        {
            new Event { Id = 1, Name = "Concert", StartDateTime = DateTime.Now, EndTime = DateTime.Now.AddHours(2) },
            new Event { Id = 2, Name = "Conference", StartDateTime = DateTime.Now.AddDays(1), EndTime = DateTime.Now.AddDays(1).AddHours(3) }
        };

        await elasticSearchService.CreateIndexIfNotExistsAsync("events");
        var result = await elasticSearchService.AddOrUpdateBulkAsync(events, "events");
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> SearchEvents(
        string eventTypeId = null,
        string startDate = null,
        string endDate = null,
        string searchTerm = null,
        string sort = "asc",
        int page = 1,
        int pageSize = 12,
        bool archived = false,
        bool onlyMasterEvents = true)
    {
        try
        {
            DateTime today = DateTime.Today;
            DateTime? parsedStartDate = null;
            DateTime? parsedEndDate = null;

            if (!string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, out DateTime startDateValue))
                parsedStartDate = startDateValue.Date;

            if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out DateTime endDateValue))
                parsedEndDate = endDateValue.Date.AddDays(1).AddSeconds(-1);

            var query = context.Event
                .Include(e => e.Venue)
                .ThenInclude(v => v.City)
                .Include(e => e.ChildEvents)
                .ThenInclude(c => c.Venue)
                .ThenInclude(v => v.City)
                .AsQueryable();

            if (!archived) query = query.Where(e => e.EndTime >= today);
            if (onlyMasterEvents) query = query.Where(e => e.ParentEventId == null);
            if (!string.IsNullOrWhiteSpace(eventTypeId) && int.TryParse(eventTypeId, out int eventTypeIdValue))
                query = query.Where(e => e.EventTypeId == eventTypeIdValue);

            if (parsedStartDate.HasValue) query = query.Where(e => e.StartDateTime >= parsedStartDate.Value);
            if (parsedEndDate.HasValue) query = query.Where(e => e.StartDateTime <= parsedEndDate.Value);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower();
                query = query.Where(e =>
                    e.Name.ToLower().Contains(term) ||
                    (e.Venue != null && e.Venue.Name.ToLower().Contains(term)) ||
                    (e.Venue != null && e.Venue.City != null && e.Venue.City.Name.ToLower().Contains(term)) ||
                    e.ChildEvents.Any(c => 
                        (c.Venue != null && c.Venue.Name.ToLower().Contains(term)) ||
                        (c.Venue != null && c.Venue.City != null && c.Venue.City.Name.ToLower().Contains(term))
                    )
                );
            }

            if (sort == "desc") query = query.OrderByDescending(e => e.EndTime);
            else query = query.OrderBy(e => e.StartDateTime);

            var totalCount = await query.CountAsync();

            var events = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.StartDateTime,
                    e.EndTime,
                    ImagePath = !string.IsNullOrEmpty(e.ImagePath) 
                        ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=800") 
                        : (e.ParentEvent != null && !string.IsNullOrEmpty(e.ParentEvent.ImagePath) 
                            ? (e.ParentEvent.ImagePath.EndsWith(".webp") ? e.ParentEvent.ImagePath : $"/Events/GetCompressedImage?path={e.ParentEvent.ImagePath}&width=800") 
                            : null),
                    VenueName = e.Venue != null ? e.Venue.Name : "No Venue",
                    CityName = (e.Venue != null && e.Venue.City != null) ? e.Venue.City.Name : "N/A",
                    parentEventId = e.ParentEventId,
                    childCount = context.Event.Count(c => c.ParentEventId == e.Id),
                    hasMultipleVenues = e.ChildEvents.Any(c => c.VenueId != null && c.VenueId != e.VenueId),
                    distinctCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count(),
                    hasMultipleCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count() > 1,
                    cityNames = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Name).Distinct().ToList()
                })
                .ToListAsync();

            return Json(new { events, totalCount, currentPage = page, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error performing event query parameters search");
            return StatusCode(500, "An error occurred during query execution");
        }
    }

    [HttpPost]
    public async Task<IActionResult> IndexAllEventsToElastic()
    {
        try
        {
            var events = await context.Event.Include(e => e.Venue).Include(e => e.EventType).ToListAsync();
            await elasticSearchService.CreateIndexIfNotExistsAsync("events");
            
            const int batchSize = 50;
            var successCount = 0;

            for (int i = 0; i < events.Count; i += batchSize)
            {
                var batch = events.Skip(i).Take(batchSize).ToList();
                var result = await elasticSearchService.AddOrUpdateBulkAsync(batch, "events");
                if (result) successCount += batch.Count;
            }

            return Ok($"Successfully indexed {successCount} of {events.Count} records.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error encountered executing bulk operation: {ex.Message}");
        }
    }

    [HttpGet("test-elasticsearch")]
    [AllowAnonymous] 
    public async Task<IActionResult> TestElasticsearch()
    {
        try
        {
            var indexName = "test-index";
            var createResult = await elasticSearchService.CreateIndexIfNotExistsAsync(indexName);
            if (!createResult) return BadRequest("Failed to initialize target index parameter");

            var testEvent = new Event
            {
                Id = 999,
                Name = "Test Event " + DateTime.Now.Ticks,
                StartDateTime = DateTime.Now,
                EndTime = DateTime.Now.AddHours(1),
                EventTypeId = 1,
                VenueId = 1
            };

            var indexResult = await elasticSearchService.AddOrUpdateAsync(testEvent, indexName);
            if (!indexResult) return BadRequest("Failed to construct test document payload");

            var searchResults = await elasticSearchService.SearchAsync<Event>("Test Event", indexName);
            return Ok(new { message = "Service connection verified.", indexCreated = createResult, documentIndexed = indexResult, searchResults = searchResults.Select(e => new { e.Id, e.Name, e.StartDateTime }) });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test operation failed during execution.");
            return BadRequest($"Execution failed: {ex.Message}");
        }
    }

    [HttpGet]
    [Route("Events/GenerateEvents/{count}")]
    public async Task<IActionResult> GenerateEvents(int count)
    {
        if (count <= 0 || count > 500) return BadRequest("Count parameters require boundaries between 1 and 500.");

        var now = DateTime.Now;
        var eventTypes = await context.EventType.ToListAsync();
        var venues = await context.Venue.ToListAsync();

        if (!eventTypes.Any() || !venues.Any()) return BadRequest("Required reference data instances are unavailable.");

        var random = new Random();
        var generatedEvents = new List<Event>();

        for (int i = 0; i < count; i++)
        {
            var eventType = eventTypes[random.Next(eventTypes.Count)];
            var venue = venues[random.Next(venues.Count)];
            var startDate = now.AddDays(random.Next(1, 5));

            var newEvent = new Event
            {
                Name = $"Generated Event {i + 1}",
                StartDateTime = startDate,
                EndTime = startDate.AddHours(random.Next(1, 5)),
                EventTypeId = eventType.Id,
                VenueId = venue.Id
            };

            context.Event.Add(newEvent);
            generatedEvents.Add(newEvent);
        }

        await context.SaveChangesAsync();
        return Json(new { success = true, events = generatedEvents });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetUserEvents()
    {
        try
        {
            var events = await context.Event
                .OrderByDescending(e => e.StartDateTime)
                .Select(e => new {
                    id = e.Id,
                    name = e.Name,
                    startDateTime = e.StartDateTime,
                    endTime = e.EndTime,
                    venueId = e.VenueId,
                    venue = e.Venue != null ? new { name = e.Venue.Name } : null,
                    eventType = e.EventType != null ? new { name = e.EventType.Name } : null,
                    organizerId = e.OrganizerId
                })
                .ToListAsync();

            return Json(new { success = true, events = events });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, inner = ex.InnerException?.Message });
        }
    }

    [AllowAnonymous]
    public async Task<IActionResult> GetAutocompleteResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Json(new List<object>());

        query = query.ToLower();
        var results = new List<object>();
        var maxResults = 5;

        try
        {
            var eventResults = await context.Event
                .Where(e => e.Name.ToLower().Contains(query))
                .OrderBy(e => e.Name)
                .Take(maxResults)
                .Select(e => new { text = e.Name, type = "event", subtext = $"Event on {e.StartDateTime.ToString("MMM d, yyyy")}", id = e.Id })
                .ToListAsync();

            results.AddRange(eventResults);

            if (results.Count < maxResults)
            {
                var venueResults = await context.Venue
                    .Where(v => v.Name.ToLower().Contains(query))
                    .OrderBy(v => v.Name)
                    .Take(maxResults - results.Count)
                    .Select(v => new { text = v.Name, type = "location", subtext = v.City != null ? $"Venue in {v.City.Name}" : "Venue", id = v.Id })
                    .ToListAsync();

                results.AddRange(venueResults);
            }

            if (results.Count < maxResults)
            {
                var cityResults = await context.City
                    .Where(c => c.Name.ToLower().Contains(query))
                    .OrderBy(c => c.Name)
                    .Take(maxResults - results.Count)
                    .Select(c => new { text = c.Name, type = "location", subtext = "City", id = c.Id })
                    .ToListAsync();

                results.AddRange(cityResults);
            }

            return Json(results);
        }
        catch (Exception)
        {
            return Json(new List<object>());
        }
    }

    [HttpGet]
    public JsonResult GetLayouts(int venueId)
    {
        var layouts = context.Layout
            .Where(sa => sa.VenueId == venueId)
            .Select(sa => new { id = sa.Id, areaName = sa.AreaName })
            .ToList();

        return Json(layouts);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> EditSubSelectedName(int id, string NewName)
    {
        try
        {
            var userId = userManager.GetUserId(User);
            var originalEvent = await context.Event.Include(e => e.Venue).FirstOrDefaultAsync(e => e.Id == id);
            
            if (originalEvent == null) return NotFound();
            if (originalEvent.Venue.UserId != userId && !User.IsInRole("Admin")) return Unauthorized();

            originalEvent.Name = NewName;
            await context.SaveChangesAsync();
        }
        catch (Exception)
        {
            return NotFound();
        }
        return Json(new { success = true, message = "Update query successfully processed." });
    }

    [Authorize] 
    [HttpGet]
    public async Task<IActionResult> SearchParentEvents(string query)
    {
        var events = await context.Event
            .Where(e => e.ParentEventId == null && e.Name.Contains(query))
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                venue = e.Venue.Name,
                venueId = e.VenueId,
                eventTypeId = e.EventTypeId,
                rawStartDate = e.StartDateTime.ToString("yyyy-MM-ddTHH:mm"),
                rawEndDate = e.EndTime.ToString("yyyy-MM-ddTHH:mm"),
                date = e.StartDateTime.ToString("MMM dd, yyyy")
            })
            .Take(5)
            .ToListAsync();
        return Json(events);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSubEvents(int parentId)
    {
        var subEvents = await context.Event
            .Include(e => e.Layout)
            .Where(e => e.ParentEventId == parentId)
            .OrderBy(e => e.StartDateTime)
            .Select(e => new {
                id = e.Id,
                name = e.Name, 
                date = e.StartDateTime.ToString("dddd, MMM d, yyyy"),
                time = e.StartDateTime.ToString("h:mm tt") + " - " + e.EndTime.ToString("h:mm tt"),
                layout = e.Layout.AreaName
            })
            .ToListAsync();
            
        return Json(subEvents);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> UpdateParentEvent([FromBody] LinkEventDto data)
    {
        try
        {
            var userId = userManager.GetUserId(User);
            var childEvent = await context.Event.Include(e => e.Venue).FirstOrDefaultAsync(e => e.Id == data.ChildId);

            if (childEvent == null) return NotFound(new { success = false, message = "Requested identifier missing." });
            if (childEvent.Venue.UserId != userId && !User.IsInRole("Admin")) return Unauthorized();

            childEvent.ParentEventId = data.ParentId;
            await context.SaveChangesAsync();

            return Json(new { success = true });
        }
        catch (Exception)
        {
            return StatusCode(500, new { success = false, message = "System exception encountered." });
        }
    }

    public class LinkEventDto
    {
        public int ChildId { get; set; }
        public int? ParentId { get; set; }
    }

    [AllowAnonymous]
    public async Task<IActionResult> ChildEvents(int? id)
    {
        if (id == null) return NotFound();

        var fatherEvent = await context.Event.FirstOrDefaultAsync(e => e.Id == id);
        if (fatherEvent == null) return NotFound();

        var childEvents = await context.Event
            .Include(e => e.Venue)
            .Include(e => e.Venue.City)
            .Include(e => e.EventType)
            .Where(e => e.ParentEventId == id)
            .OrderBy(e => e.StartDateTime)
            .ToListAsync();

        ViewBag.FatherEventName = fatherEvent.Name;
        ViewBag.FatherEventId = fatherEvent.Id;
        return View(childEvents);
    }

    [HttpGet]
    public async Task<IActionResult> SearchVenues(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Json(new List<object>());

        query = query.ToLower();
        var venues = await context.Venue
            .Where(v => v.Name.ToLower().Contains(query) || (v.City != null && v.City.Name.ToLower().Contains(query)))
            .OrderByDescending(v => v.Name.ToLower().Contains(query))
            .ThenBy(v => v.Name)
            .Select(v => new { id = v.Id, name = v.Name, city = v.City != null ? v.City.Name : "N/A" })
            .Take(10)
            .ToListAsync();

        return Json(venues);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> DuplicateSubEvent(int id)
    {
        var ev = await context.Event.Include(e => e.Venue).AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (ev == null) return NotFound();

        var userId = userManager.GetUserId(User);
        if (ev.Venue.UserId != userId && !User.IsInRole("Admin")) return Unauthorized();

        var newEvent = new Event
        {
            Name = GenerateNextName(ev.Name),
            StartDateTime = ev.StartDateTime,
            EndTime = ev.EndTime,
            EventTypeId = ev.EventTypeId,
            VenueId = ev.VenueId,
            LayoutId = ev.LayoutId,
            ParentEventId = ev.ParentEventId,
            ImagePath = ev.ImagePath
        };

        context.Event.Add(newEvent);
        await context.SaveChangesAsync();

        return Json(new { success = true, id = newEvent.Id });
    }

    private string GenerateNextName(string currentName)
    {
        if (string.IsNullOrWhiteSpace(currentName)) return "New Event 1";
        var match = System.Text.RegularExpressions.Regex.Match(currentName, @"(\d+)$");

        if (match.Success)
        {
            string numberStr = match.Value;
            if (int.TryParse(numberStr, out int number))
            {
                string baseName = currentName.Substring(0, match.Index).TrimEnd();
                return $"{baseName} {number + 1}";
            }
        }
        return $"{currentName.TrimEnd()} 1";
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetEventTiming(int id)
    {
        var ev = await context.Event.Where(e => e.Id == id)
            .Select(e => new { start = e.StartDateTime.ToString("yyyy-MM-ddTHH:mm"), end = e.EndTime.ToString("yyyy-MM-ddTHH:mm") })
            .FirstOrDefaultAsync();

        if (ev == null) return NotFound();
        return Json(ev);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> MultiSubRename([FromForm] List<int> ids, [FromForm] string NewName)
    {
        try
        {
            var eventsToRename = await context.Event.Include(e => e.Venue).Where(e => ids.Contains(e.Id)).ToListAsync();
            var userId = userManager.GetUserId(User);

            if (eventsToRename.Any(e => e.Venue.UserId != userId) && !User.IsInRole("Admin")) return Unauthorized();

            var count = 1;
            foreach (var ev in eventsToRename)
            {
                ev.Name = $"{NewName} ({count})";
                count++;
            }

            await context.SaveChangesAsync();
            return Json(new { success = true, message = "Entity properties mapped and updated." });
        }
        catch (Exception)
        {
            return BadRequest(new { success = false, message = "Exception executing state modifications." });
        }
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> getNewestEvents()
    {
        var ev = await context.Event
            .Where(e => e.ParentEventId == null && e.EndTime > DateTime.Now)
            .OrderByDescending(e => e.Id)
            .Take(5)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=800") 
                    : null,
                eventType = e.EventType != null ? e.EventType.Name : "Default",
                distinctCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count(),
                hasMultipleCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count() > 1,
                cityNames = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Name).Distinct().ToList()
            })
            .ToListAsync();
            
        return Json(ev);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult HomePage() => View();

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> getRecentEvents()
    {
        var ev = await context.Event
            .Where(e => e.ParentEventId == null && e.EndTime > DateTime.Now)
            .OrderBy(e => e.StartDateTime)
            .Take(5)
            .Select(e => new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                venueName = e.Venue.Name,
                cityName = e.Venue.City != null ? e.Venue.City.Name : "N/A",
                imagePath = !string.IsNullOrEmpty(e.ImagePath) 
                    ? (e.ImagePath.EndsWith(".webp") ? e.ImagePath : $"/Events/GetCompressedImage?path={e.ImagePath}&width=800") 
                    : null,
                eventType = e.EventType != null ? e.EventType.Name : "Default",
                distinctCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count(),
                hasMultipleCities = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Id).Distinct().Count() > 1,
                cityNames = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Name).Distinct().ToList()
            })
            .ToListAsync();
            
        return Json(ev);
    }
}