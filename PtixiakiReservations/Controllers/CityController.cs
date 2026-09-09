using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using System.Linq;
using System.Threading.Tasks;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages administrative operations for operational regions (Cities).
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class CityController : Controller
    {        
        private readonly ApplicationDbContext _context;

        public CityController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Retrieves a paginated and searchable list of registered cities.
        /// </summary>
        /// <param name="searchQuery">Optional text to filter cities by name.</param>
        /// <param name="pageNumber">The current page index representing the data offset.</param>
        [HttpGet]
        public async Task<IActionResult> ManageCities(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            var query = _context.City.AsQueryable();

            // Apply search filters.
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(c => c.Name.ToLower().Contains(normalizedQuery));
            }

            // Calculate pagination metadata.
            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            // Retrieve paginated records.
            var cities = await query
                .OrderBy(c => c.Name) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Populate view context.
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(cities);
        }

        /// <summary>
        /// Renders the city creation interface.
        /// </summary>
        [HttpGet]
        public IActionResult CreateCity()
        {
            return View();
        }

        /// <summary>
        /// Validates and creates a new city, ensuring no duplicate entries exist.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCity(string CityName)
        {
            // Prevent empty submissions.
            if (string.IsNullOrWhiteSpace(CityName))
            {
                return View();
            }

            var cleanCityName = CityName.Trim();

            // Prevent duplicate entries.
            bool cityExists = await _context.City
                .AnyAsync(c => c.Name.ToLower() == cleanCityName.ToLower());

            if (!cityExists)
            {
                _context.City.Add(new City { Name = cleanCityName });
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ManageCities));
            }

            // Return view with error if duplicate exists.
            ModelState.AddModelError("CityName", "This city already exists.");
            return View();
        }

        /// <summary>
        /// Permanently removes a city from the database.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCity(int id)
        {
            var city = await _context.City.FindAsync(id);

            if (city != null)
            {
                _context.City.Remove(city);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(ManageCities));
        }

        /// <summary>
        /// Retrieves a specific city and renders the edit interface.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> EditCity(int CityId)
        {
            var city = await _context.City.FindAsync(CityId);

            if (city == null)
            {
                return NotFound();
            }

            return View(city);
        }

        /// <summary>
        /// Commits modifications to an existing city.
        /// </summary>
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
            else
            {
                return NoContent();
            }
        }
    }
}