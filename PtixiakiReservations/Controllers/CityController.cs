using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages administrative operations for operational regions (Cities).
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class CityController : Controller
    {        
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CityController> _logger;

        public CityController(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<CityController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ManageCities(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            var query = _context.City.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(c => c.Name.ToLower().Contains(normalizedQuery));
            }

            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var cities = await query
                .OrderBy(c => c.Name) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(cities);
        }

        [HttpGet]
        public IActionResult CreateCity()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCity(string CityName)
        {
            if (string.IsNullOrWhiteSpace(CityName)) return View();

            var cleanCityName = CityName.Trim();

            bool cityExists = await _context.City.AnyAsync(c => c.Name.ToLower() == cleanCityName.ToLower());

            if (!cityExists)
            {
                _context.City.Add(new City { Name = cleanCityName });
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ManageCities));
            }

            ModelState.AddModelError("CityName", "This city already exists.");
            return View();
        }

        /// <summary>
        /// Permanently removes a city from the database. 
        /// Uses high-performance bulk operations (ExecuteDeleteAsync) to prevent RAM OOM crashes.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCity(int id)
        {
            var city = await _context.City.FindAsync(id);
            if (city == null) return RedirectToAction(nameof(ManageCities));

            try
            {
                // 0. Detach all Users from this City to prevent RESTRICT FK violation
                // This instantly sets their CityId to null without loading users into RAM
                await _context.Users
                    .Where(u => u.CityId == id)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.CityId, (int?)null));

                // 1. Gather IDs via projection (Uses minimal RAM - no entity tracking)
                var venueIds = await _context.Venue.Where(v => v.CityId == id).Select(v => v.Id).ToListAsync();

                if (venueIds.Any())
                {
                    var layoutIds = await _context.Layout.Where(l => venueIds.Contains(l.VenueId)).Select(l => l.Id).ToListAsync();

                    var masterEventIds = await _context.Event
                        .Where(e => (e.VenueId.HasValue && venueIds.Contains(e.VenueId.Value)) || 
                                    (e.LayoutId.HasValue && layoutIds.Contains(e.LayoutId.Value)))
                        .Select(e => e.Id)
                        .ToListAsync();

                    var childEventIds = await _context.Event
                        .Where(e => e.ParentEventId.HasValue && masterEventIds.Contains(e.ParentEventId.Value))
                        .Select(e => e.Id)
                        .ToListAsync();

                    var allEventIds = masterEventIds.Concat(childEventIds).Distinct().ToList();

                    // 2. Harvest physical file paths before we wipe the DB records
                    var eventImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync();
                    var galleryImagePaths = await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync();
                    var venueImagePaths = await _context.Venue.Where(v => venueIds.Contains(v.Id)).Select(v => v.imgUrl).ToListAsync();

                    // =======================================================================
                    // 3. EXECUTE BULK DB DELETIONS (Translates directly to raw SQL)
                    // =======================================================================

                    if (allEventIds.Any())
                    {
                        await _context.Reservation.Where(r => allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();
                        await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();
                        
                        if (childEventIds.Any())
                            await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                            
                        if (masterEventIds.Any())
                            await _context.Event.Where(e => masterEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    }

                    if (layoutIds.Any())
                    {
                        await _context.Seat.Where(s => layoutIds.Contains(s.LayoutId)).ExecuteDeleteAsync();
                        await _context.NonSelectable.Where(ns => layoutIds.Contains(ns.LayoutId)).ExecuteDeleteAsync();
                        await _context.UnitGroup.Where(ug => layoutIds.Contains(ug.LayoutId)).ExecuteDeleteAsync();
                        await _context.Layout.Where(l => layoutIds.Contains(l.Id)).ExecuteDeleteAsync();
                    }

                    await _context.VenueCategory.Where(vc => venueIds.Contains(vc.VenueId)).ExecuteDeleteAsync();
                    await _context.Venue.Where(v => venueIds.Contains(v.Id)).ExecuteDeleteAsync();

                    // =======================================================================
                    // 4. CLEAN UP PHYSICAL DISK STORAGE
                    // =======================================================================
                    
                    var allPathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
                    foreach (var path in allPathsToDelete)
                    {
                        string fullPath = Path.Combine(_environment.WebRootPath, path.TrimStart('/', '\\'));
                        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                    }

                    foreach (var vImg in venueImagePaths.Where(p => !string.IsNullOrEmpty(p)))
                    {
                        string fullPath = Path.Combine(_environment.WebRootPath, "images", vImg.TrimStart('/', '\\'));
                        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                    }
                }

                _context.City.Remove(city);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute memory-safe cascading deletion for City ID {Id}", id);
            }
            
            return RedirectToAction(nameof(ManageCities));
        }

        [HttpGet]
        public async Task<IActionResult> EditCity(int CityId)
        {
            var city = await _context.City.FindAsync(CityId);
            if (city == null) return NotFound();
            return View(city);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCity(int CityId, string CityName)
        {
            var city = await _context.City.FindAsync(CityId);

            if(city != null && !string.IsNullOrWhiteSpace(CityName))
            {
                city.Name = CityName.Trim();
                _context.Update(city);
                await _context.SaveChangesAsync();
                
                return RedirectToAction(nameof(ManageCities));
            }
            return NoContent();
        }
    }
}