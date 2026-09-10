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
/// Manages event lifecycles, hierarchical structures, media transcoding, and Elasticsearch indexing.
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
    /// Dynamically transcodes and compresses images to WebP format.
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
                    : null,
                ticketPrice = e.TicketPrice,
                minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
                    : null,
                ticketPrice = e.TicketPrice,
                minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
                    : null,
                ticketPrice = e.TicketPrice,
                minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
                    : null,
                ticketPrice = e.TicketPrice,
                minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
                (e.Venue != null && e.Venue.City != null && e.Venue.City.Name.ToLower().Contains(term)) ||
                e.ChildEvents.Any(c => 
                    c.Name.ToLower().Contains(term) || 
                    (c.Venue != null && c.Venue.Name.ToLower().Contains(term)) ||
                    (c.Venue != null && c.Venue.City != null && c.Venue.City.Name.ToLower().Contains(term))
                )
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
                venueName = e.Venue.Name,
                ticketPrice = e.TicketPrice,
                minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
            eventType = e.EventType != null ? e.EventType.Name : "Default",
            ticketPrice = e.TicketPrice,
            minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
            maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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

        // Validate dependencies for standalone or child events.
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

        // Verify parent constraints.
        Event parentEvent = null;
        if (isChild)
        {
            parentEvent = await context.Event.AsNoTracking().FirstOrDefaultAsync(e => e.Id == newEvent.ParentEventId.Value);
            if (parentEvent == null)
            {
                return BadRequest(new { success = false, message = "The assigned Master Event does not exist." });
            }
        }

        PtixiakiReservations.Models.Venue venue = null;
        if (newEvent.VenueId.HasValue)
        {
            venue = await context.Venue.FirstOrDefaultAsync(v => v.Id == newEvent.VenueId);
            if (venue == null) return BadRequest(new { success = false, message = "Venue does not exist." });
        }

        var userId = userManager.GetUserId(User);
        newEvent.OrganizerId = userId;

        // Process and store the primary image asset.
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

        // Process and store gallery image assets.
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
                        var eventStart = date.Date.Add(startTimeSpan);
                        var eventEnd = date.Date.Add(endTimeSpan);

                        // Validate sub-event bounds for multi-day schedules.
                        if (isChild && parentEvent != null)
                        {
                            if (eventStart < parentEvent.StartDateTime || eventEnd > parentEvent.EndTime)
                            {
                                return BadRequest(new { 
                                    success = false, 
                                    message = $"Sub-event timeframe securely blocked. It falls strictly outside the Master Event's allowed timeframe ({parentEvent.StartDateTime:MMM d, yyyy h:mm tt} - {parentEvent.EndTime:MMM d, yyyy h:mm tt})." 
                                });
                            }
                        }

                        var eventForDay = new Event
                        {
                            Name = newEvent.Name + " Day " + count,
                            Description = (isChild && parentEvent != null) ? parentEvent.Description : newEvent.Description,
                            EventTypeId = (isChild && parentEvent != null) ? parentEvent.EventTypeId : newEvent.EventTypeId,
                            ImagePath = (isChild && parentEvent != null && string.IsNullOrEmpty(newEvent.ImagePath)) ? parentEvent.ImagePath : newEvent.ImagePath,
                            
                            TicketPrice = newEvent.TicketPrice,
                            VenueId = newEvent.VenueId, 
                            LayoutId = newEvent.LayoutId, 
                            StartDateTime = eventStart,
                            EndTime = eventEnd,
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

                // Validate sub-event bounds for single-day schedules.
                if (isChild && parentEvent != null)
                {
                    if (newEvent.StartDateTime < parentEvent.StartDateTime || newEvent.EndTime > parentEvent.EndTime)
                    {
                        return BadRequest(new { 
                            success = false, 
                            message = $"Sub-event timeframe securely blocked. It falls strictly outside the Master Event's allowed timeframe ({parentEvent.StartDateTime:MMM d, yyyy h:mm tt} - {parentEvent.EndTime:MMM d, yyyy h:mm tt})." 
                        });
                    }
                    
                    newEvent.Description = parentEvent.Description;
                    newEvent.EventTypeId = parentEvent.EventTypeId;
                    if (string.IsNullOrEmpty(newEvent.ImagePath)) newEvent.ImagePath = parentEvent.ImagePath;
                }

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
    /// Initializes the modification interface for an existing event.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        // Include required relational data for the edit interface.
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

        // Populate ViewBag collections for dropdown selections.
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
    /// Processes updates to an existing event, including schedule boundaries and media transcoding.
    /// </summary>
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

                // Ensure updated timeframe remains within parent bounds.
                if (originalEvent.ParentEventId != null)
                {
                    var parent = await context.Event.AsNoTracking().FirstOrDefaultAsync(e => e.Id == originalEvent.ParentEventId);
                    if (parent != null)
                    {
                        if (updatedEvent.StartDateTime < parent.StartDateTime || updatedEvent.EndTime > parent.EndTime)
                        {
                            ModelState.AddModelError("", $"Sub-event timeframe strictly falls outside the Master Event's allowed timeframe ({parent.StartDateTime:MMM d, yyyy h:mm tt} - {parent.EndTime:MMM d, yyyy h:mm tt}).");
                            
                            updatedEvent.ImagePath = originalEvent.ImagePath;
                            updatedEvent.GalleryImages = originalEvent.GalleryImages;
                            updatedEvent.ParentEventId = originalEvent.ParentEventId;
                            return View(updatedEvent);
                        }
                    }
                }

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

                    if (originalEvent.GalleryImages != null && originalEvent.GalleryImages.Any())
                    {
                        foreach (var oldImg in originalEvent.GalleryImages)
                        {
                            string oldPath = Path.Combine(environment.WebRootPath, oldImg.ImagePath.TrimStart('/', '\\'));
                            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                        }
                        context.RemoveRange(originalEvent.GalleryImages);
                        originalEvent.GalleryImages.Clear();
                    }

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
                originalEvent.TicketPrice = updatedEvent.TicketPrice;
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
                
                // Return to Index after successful edit
                return RedirectToAction(nameof(Index));
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

    
    /// <summary>
    /// Permanently removes an event and cascades deletions down to Wishlists, Reservations, Sub-Events, and Gallery Images.
    /// Utilizes high-performance ExecuteDeleteAsync bulk commands to bypass RAM constraints and RESTRICT locks.
    /// </summary>
    [Authorize]
    [HttpDelete]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var ev = await context.Event.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
            
        if (ev == null) return NotFound();

        var userId = userManager.GetUserId(User);
        if (ev.OrganizerId != userId && !User.IsInRole("Admin")) return Unauthorized();

        try 
        {
            // 1. Gather all Event IDs (The target event + any children if it's a master)
            var allEventIds = new List<int> { ev.Id };
            
            var childEventIds = await context.Event
                .Where(e => e.ParentEventId == id)
                .Select(e => e.Id)
                .ToListAsync();
                
            if (childEventIds.Any())
            {
                allEventIds.AddRange(childEventIds);
            }

            // 2. Harvest physical file paths to delete from disk later
            var eventImagePaths = await context.Event
                .Where(e => allEventIds.Contains(e.Id))
                .Select(e => e.ImagePath)
                .ToListAsync();
                
            var galleryImagePaths = await context.Event
                .Where(e => allEventIds.Contains(e.Id))
                .SelectMany(e => e.GalleryImages)
                .Select(g => g.ImagePath)
                .ToListAsync();

            // =======================================================================
            // 3. EXECUTE BULK DB DELETIONS (Translates directly to raw SQL)
            // =======================================================================
            
            // Delete Wishlists (safely bypasses RESTRICT foreign key constraint)
            await context.Set<Wishlist>().Where(w => allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();
            
            // Delete Reservations
            await context.Reservation.Where(r => allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();
            
            // Delete Gallery Images (safely bypasses RESTRICT foreign key constraint)
            await context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();
            
            // Delete Child Events
            if (childEventIds.Any())
            {
                await context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
            }
            
            // Delete Master Event
            await context.Event.Where(e => e.Id == id).ExecuteDeleteAsync();

            // =======================================================================
            // 4. CLEAN UP PHYSICAL DISK STORAGE
            // =======================================================================
            var allPathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
            foreach (var path in allPathsToDelete)
            {
                string fullPath = Path.Combine(environment.WebRootPath, path.TrimStart('/', '\\'));
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute memory-safe cascading deletion for Event ID {Id}", id);
            return BadRequest(new { success = false, message = "Could not delete event due to constrained related data: " + ex.Message });
        }
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
                        c.Name.ToLower().Contains(term) || 
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
                    cityNames = e.ChildEvents.Where(c => c.Venue != null && c.Venue.City != null).Select(c => c.Venue.City.Name).Distinct().ToList(),
                    ticketPrice = e.TicketPrice,
                    minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                    maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
            var userId = userManager.GetUserId(User);
            var events = await context.Event
                .Where(e => e.OrganizerId == userId) // Fixed: Database-level filtering for optimization
                .OrderByDescending(e => e.StartDateTime)
                .Select(e => new {
                    id = e.Id,
                    name = e.Name,
                    startDateTime = e.StartDateTime,
                    endTime = e.EndTime,
                    venueId = e.VenueId,
                    venue = e.Venue != null ? new { name = e.Venue.Name } : null,
                    eventType = e.EventType != null ? new { name = e.EventType.Name } : null,
                    organizerId = e.OrganizerId,
                    parentEventId = e.ParentEventId, // Fixed: Added Parent ID tracking for frontend UI render checks
                    ticketPrice = e.TicketPrice,
                    minPrice = e.ChildEvents.Any() ? e.ChildEvents.Min(c => c.TicketPrice) : e.TicketPrice,
                    maxPrice = e.ChildEvents.Any() ? e.ChildEvents.Max(c => c.TicketPrice) : e.TicketPrice
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
            var originalEvent = await context.Event.FirstOrDefaultAsync(e => e.Id == id);
            
            if (originalEvent == null) return NotFound();
            
            // Fixed: Check owner on event Organizer instead of Venue (which threw 500 Null Reference crashes if missing)
            if (originalEvent.OrganizerId != userId && !User.IsInRole("Admin")) return Unauthorized();

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
                layout = e.Layout != null ? e.Layout.AreaName : "No Layout",
                ticketPrice = e.TicketPrice
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
            var childEvent = await context.Event.FirstOrDefaultAsync(e => e.Id == data.ChildId);

            if (childEvent == null) return NotFound(new { success = false, message = "Requested identifier missing." });
            if (childEvent.OrganizerId != userId && !User.IsInRole("Admin")) return Unauthorized();

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
        var ev = await context.Event.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
        if (ev == null) return NotFound();

        var userId = userManager.GetUserId(User);
        if (ev.OrganizerId != userId && !User.IsInRole("Admin")) return Unauthorized();

        var newEvent = new Event
        {
            Name = GenerateNextName(ev.Name),
            TicketPrice = ev.TicketPrice,
            StartDateTime = ev.StartDateTime,
            EndTime = ev.EndTime,
            EventTypeId = ev.EventTypeId,
            VenueId = ev.VenueId,
            LayoutId = ev.LayoutId,
            ParentEventId = ev.ParentEventId,
            OrganizerId = userId,
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
            var eventsToRename = await context.Event.Where(e => ids.Contains(e.Id)).ToListAsync();
            var userId = userManager.GetUserId(User);

            if (eventsToRename.Any(e => e.OrganizerId != userId) && !User.IsInRole("Admin")) return Unauthorized();

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
    public IActionResult HomePage() => View();

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> getNewestEvents()
    {
        var events = await context.Event
            .Include(e => e.EventType)
            .Include(e => e.Venue)
                .ThenInclude(v => v.City)
            .Where(e => e.ParentEventId == null)
            .OrderByDescending(e => e.Id) 
            .Take(12)
            .ToListAsync();

        var eventIds = events.Select(e => e.Id).ToList();
        var childEvents = await context.Event
            .Include(e => e.Venue)
                .ThenInclude(v => v.City)
            .Where(e => e.ParentEventId != null && eventIds.Contains(e.ParentEventId.Value))
            .ToListAsync();

        var result = events.Select(e => {
            var children = childEvents.Where(c => c.ParentEventId == e.Id).ToList();
            var childPrices = children.Where(c => c.TicketPrice != null).Select(c => c.TicketPrice.Value).ToList();
            
            // Find distinct cities of all child events
            var childCities = children.Where(c => c.Venue?.City?.Name != null)
                                      .Select(c => c.Venue.City.Name)
                                      .Distinct()
                                      .ToList();
            
            return new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                imagePath = e.ImagePath,
                eventType = e.EventType != null ? e.EventType.Name : null,
                venueName = e.Venue != null ? e.Venue.Name : null,
                cityName = e.Venue?.City?.Name ?? "Unknown",
                ticketPrice = e.TicketPrice,
                
                childCount = children.Count,
                minPrice = childPrices.Any() ? childPrices.Min() : (double?)null,
                maxPrice = childPrices.Any() ? childPrices.Max() : (double?)null,
                
                distinctCities = childCities.Count,
                cityNames = childCities,
                
                parentEventId = e.ParentEventId
            };
        });

        return Json(result);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> getRecentEvents()
    {
        var events = await context.Event
            .Include(e => e.EventType)
            .Include(e => e.Venue)
                .ThenInclude(v => v.City)
            .Where(e => e.ParentEventId == null)
            .OrderByDescending(e => e.StartDateTime) 
            .Take(12)
            .ToListAsync();

        var eventIds = events.Select(e => e.Id).ToList();
        var childEvents = await context.Event
            .Include(e => e.Venue)
                .ThenInclude(v => v.City)
            .Where(e => e.ParentEventId != null && eventIds.Contains(e.ParentEventId.Value))
            .ToListAsync();

        var result = events.Select(e => {
            var children = childEvents.Where(c => c.ParentEventId == e.Id).ToList();
            var childPrices = children.Where(c => c.TicketPrice != null).Select(c => c.TicketPrice.Value).ToList();
            
            // Find distinct cities of all child events
            var childCities = children.Where(c => c.Venue?.City?.Name != null)
                                      .Select(c => c.Venue.City.Name)
                                      .Distinct()
                                      .ToList();
            
            return new {
                id = e.Id,
                name = e.Name,
                startDateTime = e.StartDateTime,
                endTime = e.EndTime,
                imagePath = e.ImagePath,
                eventType = e.EventType != null ? e.EventType.Name : null,
                venueName = e.Venue != null ? e.Venue.Name : null,
                cityName = e.Venue?.City?.Name ?? "Unknown",
                ticketPrice = e.TicketPrice,
                
                childCount = children.Count,
                minPrice = childPrices.Any() ? childPrices.Min() : (double?)null,
                maxPrice = childPrices.Any() ? childPrices.Max() : (double?)null,
                
                distinctCities = childCities.Count,
                cityNames = childCities,
                
                parentEventId = e.ParentEventId
            };
        });

        return Json(result);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> EventReservations(int id, string searchTerm = "", int page = 1, int pageSize = 15)
    {
        var currentUserId = userManager.GetUserId(User);
        
        var targetEvent = await context.Event
            .Include(e => e.Venue)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (targetEvent == null) return NotFound();
        
        // Verify authorization
        if (!User.IsInRole("Admin") && targetEvent.OrganizerId != currentUserId && targetEvent.Venue?.UserId != currentUserId) 
        {
            return Forbid();
        }

        decimal unitPrice = (decimal)(targetEvent.TicketPrice ?? 0.0);

        // Query un-grouped reservations to show them individually
        var query = context.Reservation
            .Include(r => r.ApplicationUser)
            .Include(r => r.Seat)
            .Where(r => r.EventId == id);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(r => 
                (r.ApplicationUser.UserName != null && r.ApplicationUser.UserName.ToLower().Contains(term)) || 
                (r.ApplicationUser.Email != null && r.ApplicationUser.Email.ToLower().Contains(term)));
        }

        int totalRecords = await query.CountAsync();
        int totalPages = totalRecords == 0 ? 1 : (int)Math.Ceiling(totalRecords / (double)pageSize);

        var reservations = await query
            .OrderByDescending(r => r.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new EventReservationViewModel
            {
                ReservationId = r.ID,
                UserName = r.ApplicationUser != null ? r.ApplicationUser.UserName : "Unknown User",
                Email = r.ApplicationUser != null ? r.ApplicationUser.Email : "N/A",
                ReservationDate = r.Date,
                SeatCount = 1,
                SeatName = r.Seat != null ? r.Seat.Name : "N/A",
                TotalPaid = unitPrice
            })
            .ToListAsync();

        decimal totalRevenue = totalRecords * unitPrice;

        ViewBag.SearchTerm = searchTerm;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalRecords = totalRecords;
        ViewBag.TotalRevenue = totalRevenue;
        ViewBag.EventId = id;
        ViewBag.EventName = targetEvent.Name;

        return View(reservations);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReservation(int reservationId)
    {
        var currentUserId = userManager.GetUserId(User);
        
        // Find the specific reservation row
        var reservation = await context.Reservation
            .Include(r => r.Event)
                .ThenInclude(e => e.Venue)
            .FirstOrDefaultAsync(r => r.ID == reservationId);

        if (reservation == null) return NotFound(new { success = false, message = "Reservation not found." });
        
        var targetEvent = reservation.Event;

        // Verify management authorization
        if (!User.IsInRole("Admin") && targetEvent.OrganizerId != currentUserId && targetEvent.Venue?.UserId != currentUserId) 
        {
            return Forbid();
        }

        // Check if the reservation was already attended
        if (reservation.Attended == true)
        {
            return BadRequest(new { success = false, message = "Cannot delete reservation: The attendee has already checked in." });
        }

        await context.Reservation.Where(r => r.ID == reservationId).ExecuteDeleteAsync();

        return Json(new { success = true, message = "Reservation successfully deleted." });
    }

   
}