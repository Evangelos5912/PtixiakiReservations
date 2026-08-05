using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;

namespace PtixiakiReservations.Controllers
{
    public class WishlistController : Controller
    {        private readonly ApplicationDbContext _context;

        public WishlistController(ApplicationDbContext context)
        {
            _context = context;
           
        }

        [HttpGet]
        public async Task<IActionResult> returnItem(string userId, int eventId)
        {
            var wishlistItem = await _context.Wishlist
            .AnyAsync(w => w.UserId == userId && w.EventId == eventId);

            return Json(wishlistItem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> addWishlistEvent(string userId, int eventId)
        {
            if (!string.IsNullOrWhiteSpace(userId) && eventId > 0)
            {
                _context.Wishlist.Add(new Wishlist {UserId = userId, EventId = eventId});
                await _context.SaveChangesAsync();
                return Json(true);}
            return Json(false);
        }

        [HttpDelete]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> deleteWishlistEvent(string userId, int eventId)
        {
            var wishlistItem = await _context.Wishlist
            .FirstOrDefaultAsync(w => w.UserId == userId && w.EventId == eventId);



            if (wishlistItem != null)
            {
                _context.Wishlist.Remove(wishlistItem);
                await _context.SaveChangesAsync();
                return Json(true);

            }
            return Json(false);
        }

        [HttpGet]
        [Authorize] 
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier); 

            ViewBag.TotalWishlistEvents = await _context.Wishlist
                .Where(w => w.UserId == userId)
                .CountAsync();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> SearchWishlist(string userId, string searchTerm, string sort, int page = 1, int pageSize = 12)
        {
            if (string.IsNullOrEmpty(userId)) return BadRequest("User ID is required.");

            var today = DateTime.Today;

            // Start with events only in the user's wishlist
            var query = _context.Wishlist
                .Where(w => w.UserId == userId)
                .Select(w => w.Event)
                .AsQueryable();

            // Apply search filter if provided
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(e => 
                    e.Name.ToLower().Contains(term) || 
                    (e.Venue != null && e.Venue.Name.ToLower().Contains(term)) || 
                    (e.Venue != null && e.Venue.City != null && e.Venue.City.Name.ToLower().Contains(term)));
            }

            // Match sorting logic from EventsController
            if (sort == "desc") query = query.OrderByDescending(e => e.EndTime);
            else query = query.OrderBy(e => e.StartDateTime);

            var totalCount = await query.CountAsync();

            // Use the EXACT same projection as EventsController.SearchEvents
            var events = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.StartDateTime,
                    e.EndTime,
                    ImagePath = e.ImagePath ?? (e.ParentEvent != null ? e.ParentEvent.ImagePath : null),
                    VenueName = e.Venue != null ? e.Venue.Name : "No Venue",
                    CityName = (e.Venue != null && e.Venue.City != null) ? e.Venue.City.Name : "N/A",
                    parentEventId = e.ParentEventId,
                    childCount = _context.Event.Count(c => c.ParentEventId == e.Id),
                    
                    hasMultipleVenues = e.ChildEvents.Any(c => c.VenueId != null && c.VenueId != e.VenueId),
                    
                    distinctCities = e.ChildEvents
                        .Where(c => c.Venue != null && c.Venue.City != null)
                        .Select(c => c.Venue.City.Id)
                        .Distinct()
                        .Count(),
                        
                    hasMultipleCities = e.ChildEvents
                        .Where(c => c.Venue != null && c.Venue.City != null)
                        .Select(c => c.Venue.City.Id)
                        .Distinct()
                        .Count() > 1,

                    cityNames = e.ChildEvents
                        .Where(c => c.Venue != null && c.Venue.City != null)
                        .Select(c => c.Venue.City.Name)
                        .Distinct()
                        .ToList()
                })
                .ToListAsync();

            return Json(new
            {
                events,
                totalCount,
                currentPage = page,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }

        // 3. The AJAX endpoint for the search bar autocomplete
        [HttpGet]
        public async Task<IActionResult> GetAutocompleteResults(string query, string userId)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(userId)) 
                return Json(new List<object>());

            query = query.ToLower();
            var results = new List<object>();
            var maxResults = 5;

            try
            {
                // Only search within the user's wishlisted events
                var userEvents = _context.Wishlist
                    .Where(w => w.UserId == userId)
                    .Select(w => w.Event)
                    .AsQueryable();

                // Match Event names (including the 'id' field to match your JS)
                var eventResults = await userEvents
                    .Where(e => e.Name.ToLower().Contains(query))
                    .OrderBy(e => e.Name)
                    .Take(maxResults)
                    .Select(e => new {
                        text = e.Name,
                        type = "event",
                        subtext = $"Event on {e.StartDateTime.ToString("MMM d, yyyy")}",
                        id = e.Id
                    })
                    .ToListAsync();

                results.AddRange(eventResults);

                // Match Cities associated with the wishlisted events
                if (results.Count < maxResults)
                {
                    var cityResults = await userEvents
                        .Where(e => e.Venue != null && e.Venue.City != null && e.Venue.City.Name.ToLower().Contains(query))
                        .Select(e => e.Venue.City)
                        .Distinct()
                        .OrderBy(c => c.Name)
                        .Take(maxResults - results.Count)
                        .Select(c => new {
                            text = c.Name,
                            type = "location",
                            subtext = "City",
                            id = c.Id
                        })
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


    }

    
}