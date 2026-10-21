using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;
using PtixiakiReservations.Services;

namespace PtixiakiReservations.Controllers
{
    public class LayoutsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _usermanager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LayoutsController> _logger;
        private readonly IAILayoutService _aiLayoutService;
        public LayoutsController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> usermanager,
            IWebHostEnvironment environment,
            ILogger<LayoutsController> logger,
            IAILayoutService aiLayoutService)
        {
            _context = context;
            _usermanager = usermanager;
            _environment = environment;
            _logger = logger;
            _aiLayoutService = aiLayoutService;
        }

        private async Task<bool> IsAuthorizedToManageLayout(int venueId)
        {
            if (User.IsInRole("Admin")) return true;

            var userId = _usermanager.GetUserId(User);
            var venue = await _context.Venue.AsNoTracking().FirstOrDefaultAsync(v => v.Id == venueId);
            
            return venue != null && venue.UserId == userId;
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Index()
        {
            var userId = _usermanager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin");

            var query = _context.Layout.Include(l => l.Venue).AsQueryable();

            if (!isAdmin)
            {
                query = query.Where(l => l.Venue.UserId == userId);
            }

            var layouts = await query
                .Select(sa => new
                {
                    sa.Id,
                    sa.AreaName,
                    sa.Desc,
                    HasSeats = _context.Seat.Any(seat => seat.LayoutId == sa.Id)
                })
                .ToListAsync();

            ViewBag.Layouts = layouts;
            return View();
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> ChooseLayout(int venueId, int eventId, string duration, string resDate)
        {
            var venue = await _context.Venue.FindAsync(venueId);
            if (venue == null) return NotFound();

            ViewData["EventId"] = eventId;
            ViewData["VenueId"] = venueId;
            ViewData["Duration"] = duration;
            ViewData["ResDate"] = resDate;

            return View(venue);
        }
        
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Details(int? id, int? venueId)
        {
            if (id == null) return NotFound();

            var layout = await _context.Layout
                .Include(s => s.Venue)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (layout == null) return NotFound();

            if (!await IsAuthorizedToManageLayout(layout.VenueId))
            {
                return Forbid();
            }

            ViewBag.VenueId = venueId ?? layout.VenueId;
            ViewBag.VenueName = layout.Venue?.Name;

            return View(layout);
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] JsonLayoutModel[] layouts)
        {
            if (layouts == null || !layouts.Any())
            {
                return BadRequest(new { error = "No sub-areas provided" });
            }

            var userId = _usermanager.GetUserId(HttpContext.User);

            var userVenueIds = await _context.Venue
                .Where(v => v.UserId == userId)
                .Select(v => v.Id)
                .ToListAsync();

            var isAdmin = User.IsInRole("Admin");

            foreach (var layout in layouts)
            {
                if (!isAdmin && !userVenueIds.Contains(layout.VenueId))
                {
                    return Forbid(); 
                }

                Layout newLayout = new Layout
                {
                    AreaName = layout.AreaName,
                    Desc = layout.Desc,
                    Height = layout.Height,
                    Width = layout.Width,
                    Rotate = layout.Rotate,
                    Top = layout.Top,
                    Left = layout.Left,
                    VenueId = layout.VenueId 
                };
                _context.Add(newLayout);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Successfully created all sub-areas" });
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        [HttpPost]
        public async Task<IActionResult> CreateFromVenue([FromBody]JsonLayoutModel[] layouts)
        {
            if(layouts == null)
            {
                ViewBag.Error = "Something went wrong";
                return View("Error");
            }
            
            var venue = await _context.Venue.FirstOrDefaultAsync(v => v.ApplicationUser.Id == _usermanager.GetUserId(HttpContext.User));
            if (venue == null && !User.IsInRole("Admin")) return Forbid();

            foreach (var layout in layouts)
            {
                Layout newLayout = new Layout
                {
                    AreaName = layout.AreaName,
                    Height = layout.Height,
                    Width = layout.Width,
                    Rotate = layout.Rotate,
                    Top = layout.Top,
                    Left = layout.Left,
                    VenueId = venue.Id
                };
                _context.Add(newLayout);
            }
            await _context.SaveChangesAsync();

            Response.StatusCode = (int)HttpStatusCode.OK;
            return Json(Response.StatusCode);
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var layout = await _context.Layout.FindAsync(id);
            if (layout == null) return NotFound();

            if (!await IsAuthorizedToManageLayout(layout.VenueId))
            {
                return Forbid();
            }

            ViewData["VenueId"] = new SelectList(_context.Venue, "Id", "Id", layout.VenueId);
            return View(layout);
        }

        [HttpPost]
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Edit(int id, Layout layoutEdit)
        {
            var layout = await _context.Layout.SingleOrDefaultAsync(s => s.Id == id);
            
            if (layout == null || id != layout.Id) return NotFound();
            
            if (!await IsAuthorizedToManageLayout(layout.VenueId))
            {
                return Forbid();
            }
           
            if (ModelState.IsValid)
            {
                layout.AreaName = layoutEdit.AreaName;
                layout.Desc = layoutEdit.Desc;
                layout.Height = layoutEdit.Height;
                layout.Width = layoutEdit.Width;
                layout.Top = layoutEdit.Top;
                layout.Left = layoutEdit.Left;

                try
                {                   
                    _context.Update(layout);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!LayoutExists(layout.Id)) return NotFound();
                    throw;
                }
                
                return RedirectToAction("VenueLayouts", "Layouts", new { venueId = layout.VenueId });
            }
            ViewData["VenueId"] = new SelectList(_context.Venue, "Id", "Id", layout.VenueId);
            return View(layout);
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var layout = await _context.Layout
                .Include(s => s.Venue)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (layout == null) return NotFound();

            if (!await IsAuthorizedToManageLayout(layout.VenueId))
            {
                return Forbid();
            }

            return View(layout);
        }

        /// <summary>
        /// Removes a layout and associated items using bulk operations.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var layout = await _context.Layout.FindAsync(id);
            if (layout == null) return NotFound();

            var venueId = layout.VenueId;

            if (!await IsAuthorizedToManageLayout(venueId))
            {
                return Forbid();
            }

            try
            {
                var directEventIds = await _context.Event.Where(e => e.LayoutId == id).Select(e => e.Id).ToListAsync();
                
                var childEventIds = await _context.Event
                    .Where(e => e.ParentEventId.HasValue && directEventIds.Contains(e.ParentEventId.Value))
                    .Select(e => e.Id)
                    .ToListAsync();
                    
                var allEventIds = directEventIds.Concat(childEventIds).Distinct().ToList();

                var eventImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync();
                var galleryImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync();

                if (allEventIds.Any())
                {
                    await _context.Set<Wishlist>().Where(w => allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();
                    await _context.Reservation.Where(r => allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();
                    await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();

                    if (childEventIds.Any())
                        await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    
                    if (directEventIds.Any())
                        await _context.Event.Where(e => directEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                }

                await _context.Seat.Where(s => s.LayoutId == id).ExecuteDeleteAsync();
                await _context.NonSelectable.Where(ns => ns.LayoutId == id).ExecuteDeleteAsync();
                await _context.UnitGroup.Where(ug => ug.LayoutId == id).ExecuteDeleteAsync();

                _context.Layout.Remove(layout);
                await _context.SaveChangesAsync();

                var allPathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
                foreach (var path in allPathsToDelete)
                {
                    string fullPath = Path.Combine(_environment.WebRootPath, path.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute deletion for Layout ID {Id}", id);
            }

            return RedirectToAction(nameof(VenueLayouts), new { venueId = venueId });
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> VenueLayouts(int venueId)
        {
            if (venueId == 0) return NotFound();

            var venue = await _context.Venue.FindAsync(venueId);
            if (venue == null) return NotFound();

            if (!await IsAuthorizedToManageLayout(venueId))
            {
                return Forbid();
            }

            var layouts = await _context.Layout
                .Where(sa => sa.VenueId == venueId)
                .ToListAsync();

            ViewBag.VenueName = venue.Name;
            ViewBag.VenueId = venueId;

            return View(layouts);
        }

        [HttpGet]
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<JsonResult> GetLayouts(int venueId)
        {
            if (!await IsAuthorizedToManageLayout(venueId))
            {
                return Json(new { error = "Unauthorized" });
            }

            var layouts = await _context.Layout
                .Where(sa => sa.VenueId == venueId)
                .Select(sa => new { id = sa.Id, areaName = sa.AreaName, desc = sa.Desc })
                .ToListAsync();

            return Json(layouts);
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        private bool LayoutExists(int id)
        {
            return _context.Layout.Any(e => e.Id == id);
        }

        [HttpPost]
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Duplicate([FromBody] DuplicateLayoutRequest request)
        {
            var originalLayout = await _context.Layout
                .FirstOrDefaultAsync(sa => sa.Id == request.Id);

            if (originalLayout == null) return NotFound();

            if (!await IsAuthorizedToManageLayout(originalLayout.VenueId))
            {
                return Forbid();
            }

            var duplicatedLayout = new Layout
            {
                AreaName = request.Name,
                Desc = originalLayout.Desc,
                Width = originalLayout.Width,
                Height = originalLayout.Height,
                Top = originalLayout.Top,
                Left = originalLayout.Left,
                Rotate = originalLayout.Rotate,
                VenueId = originalLayout.VenueId
            };

            _context.Layout.Add(duplicatedLayout);
            await _context.SaveChangesAsync();

            var originalSeats = await _context.Seat
                .Where(s => s.LayoutId == originalLayout.Id)
                .ToListAsync();

            foreach (var seat in originalSeats)
            {
                var duplicatedSeat = new Seat
                {
                    Name = seat.Name,
                    X = seat.X,
                    Y = seat.Y,
                    Available = seat.Available,
                    LayoutId = duplicatedLayout.Id
                };

                _context.Seat.Add(duplicatedSeat);
            }

            var originalShapes = await _context.NonSelectable
                .Where(ns => ns.LayoutId == originalLayout.Id)
                .ToListAsync();

            foreach(var shape in originalShapes)
            {
                _context.NonSelectable.Add(new NonSelectable
                {
                    Name = shape.Name,
                    X = shape.X,
                    Y = shape.Y,
                    ShapeType = shape.ShapeType,
                    Width = shape.Width,
                    Height = shape.Height,
                    BackgroundColor = shape.BackgroundColor,
                    LayoutId = duplicatedLayout.Id
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
        [AllowAnonymous]
        public async Task<IActionResult> SearchLayouts(int? venueId)
        {
            if (venueId == null) return NotFound();

            var venue = await _context.Venue
                .Include(v => v.City)
                .FirstOrDefaultAsync(v => v.Id == venueId);

            if (venue == null) return NotFound();

            var layouts = await _context.Layout
                .Where(l => l.VenueId == venueId)
                .ToListAsync();

            ViewBag.Venue = venue;
            return View(layouts);
        }
        public class PromptRequest
        {
            public string Prompt { get; set; }
            public int LayoutId { get; set; }
            public int CanvasWidth { get; set; }
            public int CanvasHeight { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateFromPrompt([FromBody] PromptRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return BadRequest("Prompt cannot be empty.");

            try
            {
                // Pass the new CanvasWidth and CanvasHeight to the AI Service
                var layoutPayload = await _aiLayoutService.GenerateLayoutAsync(
                    request.Prompt, 
                    request.LayoutId, 
                    request.CanvasWidth, 
                    request.CanvasHeight
                );
                
                return Ok(layoutPayload);
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"AI Synthesis Error: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}