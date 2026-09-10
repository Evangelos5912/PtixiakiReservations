using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;

namespace PtixiakiReservations.Controllers
{
    public class LayoutsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _usermanager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LayoutsController> _logger;

        public LayoutsController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> usermanager,
            IWebHostEnvironment environment,
            ILogger<LayoutsController> logger)
        {
            _context = context;
            _usermanager = usermanager;
            _environment = environment;
            _logger = logger;
        }

        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        public async Task<IActionResult> Index()
        {
            var layouts = await _context.Layout
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

        public async Task<IActionResult> ChooseLayout(int venueId, int eventId, string duration, string resDate)
        {
            var venue = await _context.Venue.FindAsync(venueId);
            if (venue == null)
            {
                return NotFound();
            }

            ViewData["EventId"] = eventId;
            ViewData["VenueId"] = venueId;
            ViewData["Duration"] = duration;
            ViewData["ResDate"] = resDate;

            return View(venue);
        }
        
        // GET: Layouts/Details/5
        public async Task<IActionResult> Details(int? id, int? venueId)
        {
            if (id == null)
            {
                return NotFound();
            }

            var layout = await _context.Layout
                .Include(s => s.Venue)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (layout == null)
            {
                return NotFound();
            }

            // Pass venueId to the view for proper back navigation
            ViewBag.VenueId = venueId ?? layout.VenueId;
            ViewBag.VenueName = layout.Venue?.Name;

            return View(layout);
        }

        // GET: Layouts/Create
        [Authorize(Roles = "Venue,Admin,SuperOrganizer")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] JsonLayoutModel[] layouts)
        {
            if (layouts == null || !layouts.Any())
            {
                return BadRequest(new { error = "No sub-areas provided" });
            }

            var userId = _usermanager.GetUserId(HttpContext.User);

            // Get all valid Venue IDs for this user once to avoid hitting DB in a loop
            var userVenueIds = await _context.Venue
                .Where(v => v.UserId == userId)
                .Select(v => v.Id)
                .ToListAsync();

            foreach (var layout in layouts)
            {
                if (!userVenueIds.Contains(layout.VenueId))
                {
                    return Forbid(); // User trying to add areas to someone else's venue
                }

                Layout newLayout = new Layout
                {
                    AreaName = layout.AreaName,
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

        [HttpPost]
        public async Task<IActionResult> CreateFromVenue([FromBody]JsonLayoutModel[] layouts)
        {
            if(layouts == null)
            {
                ViewBag.Error = "Something went wrong";
                return View("Error");
            }
            var venue = await _context.Venue.FirstOrDefaultAsync(v => v.ApplicationUser.Id == _usermanager.GetUserId(HttpContext.User));
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

        // GET: Layouts/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var layout = await _context.Layout.FindAsync(id);
            if (layout == null)
            {
                return NotFound();
            }
            ViewData["VenueId"] = new SelectList(_context.Venue, "Id", "Id", layout.VenueId);
            return View(layout);
        }

        // POST: Layouts/Edit/5
        [HttpPost]
        public async Task<IActionResult> Edit(int id,Layout layoutEdit)
        {
            var layout = _context.Layout.SingleOrDefault(s => s.Id == id);
            if (id != layout.Id)
            {
                return NotFound();
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

        // GET: Layouts/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var layout = await _context.Layout
                .Include(s => s.Venue)
                .FirstOrDefaultAsync(m => m.Id == id);
                
            if (layout == null)
            {
                return NotFound();
            }

            return View(layout);
        }

        /// <summary>
        /// Permanently removes a layout from the database, cascading deletes to all associated Events, Wishlists, Reservations, and Sub-components.
        /// Uses high-performance bulk operations (ExecuteDeleteAsync) to prevent RAM OOM crashes.
        /// </summary>
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var layout = await _context.Layout.FindAsync(id);
            if (layout == null) return NotFound();

            var venueId = layout.VenueId;

            try
            {
                // 1. Gather Event IDs directly tied to this Layout
                var directEventIds = await _context.Event.Where(e => e.LayoutId == id).Select(e => e.Id).ToListAsync();
                
                // Fetch Child Events spawned from those Master Events
                var childEventIds = await _context.Event
                    .Where(e => e.ParentEventId.HasValue && directEventIds.Contains(e.ParentEventId.Value))
                    .Select(e => e.Id)
                    .ToListAsync();
                    
                var allEventIds = directEventIds.Concat(childEventIds).Distinct().ToList();

                // 2. Harvest physical file paths before we wipe the DB records
                var eventImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync();
                var galleryImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync();

                // =======================================================================
                // 3. EXECUTE BULK DB DELETIONS (Translates directly to raw SQL)
                // =======================================================================

                if (allEventIds.Any())
                {
                    // Clear out Wishlists and Reservations to prevent RESTRICT FK crashes
                    await _context.Set<Wishlist>().Where(w => allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();
                    await _context.Reservation.Where(r => allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();
                    await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();

                    if (childEventIds.Any())
                        await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    
                    if (directEventIds.Any())
                        await _context.Event.Where(e => directEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                }

                // Delete all internal structure items for the Layout
                await _context.Seat.Where(s => s.LayoutId == id).ExecuteDeleteAsync();
                await _context.NonSelectable.Where(ns => ns.LayoutId == id).ExecuteDeleteAsync();
                await _context.UnitGroup.Where(ug => ug.LayoutId == id).ExecuteDeleteAsync();

                // Delete the layout itself
                _context.Layout.Remove(layout);
                await _context.SaveChangesAsync();

                // =======================================================================
                // 4. CLEAN UP PHYSICAL DISK STORAGE
                // =======================================================================
                var allPathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
                foreach (var path in allPathsToDelete)
                {
                    string fullPath = Path.Combine(_environment.WebRootPath, path.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute memory-safe cascading deletion for Layout ID {Id}", id);
            }

            return RedirectToAction(nameof(VenueLayouts), new { venueId = venueId });
        }

        // GET: Layouts/VenueLayouts/5
        public async Task<IActionResult> VenueLayouts(int venueId)
        {
            if (venueId == 0)
            {
                return NotFound();
            }

            var venue = await _context.Venue.FindAsync(venueId);
            if (venue == null)
            {
                return NotFound();
            }

            var layouts = await _context.Layout
                .Where(sa => sa.VenueId == venueId)
                .ToListAsync();

            ViewBag.VenueName = venue.Name;
            ViewBag.VenueId = venueId;

            return View(layouts);
        }

        [HttpGet]
        public JsonResult GetLayouts(int venueId)
        {
            var layouts = _context.Layout
                .Where(sa => sa.VenueId == venueId)
                .Select(sa => new { id = sa.Id, areaName = sa.AreaName, desc = sa.Desc })
                .ToList();

            return Json(layouts);
        }

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

            if (originalLayout == null)
            {
                return NotFound();
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

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true
            });
        }
    }
}