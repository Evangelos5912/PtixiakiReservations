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
        /// Retrieves a paginated and searchable directory of all registered cities.
        /// Execution is deferred so the database handles filtering and counting efficiently.
        /// </summary>
        /// <param name="searchQuery">Optional text to filter cities by name.</param>
        /// <param name="pageNumber">The current page index representing the data offset.</param>
        [HttpGet]
        public async Task<IActionResult> ManageCities(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            // 1. Initialize deferred query execution against the City table
            var query = _context.City.AsQueryable();

            // 2. Apply search filters dynamically if a search string is provided
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                
                // Using EF Core's built-in translation to execute the LIKE query in SQL
                query = query.Where(c => c.Name.ToLower().Contains(normalizedQuery));
            }

            // 3. Calculate pagination metadata boundaries
            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            // Enforce safe boundary limits to prevent out-of-bounds page requests
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            // 4. Extract the exact subset of records required for the current view
            var cities = await query
                .OrderBy(c => c.Name) // Deterministic sorting required for SQL Skip/Take
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 5. Inject pagination metadata into the ViewBag for frontend UI rendering
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(cities);
        }

        /// <summary>
        /// Renders the standalone city creation form.
        /// </summary>
        [HttpGet]
        public IActionResult CreateCity()
        {
            return View();
        }

        /// <summary>
        /// Processes the creation of a new operational city.
        /// Includes database-level validation to prevent identical geographic entries.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCity(string CityName)
        {
            // Guard clause to prevent empty submissions bypassing frontend HTML5 validation
            if (string.IsNullOrWhiteSpace(CityName))
            {
                return View();
            }

            var cleanCityName = CityName.Trim();

            // Security Mechanism: Prevent duplicate city entries
            bool cityExists = await _context.City
                .AnyAsync(c => c.Name.ToLower() == cleanCityName.ToLower());

            if (!cityExists)
            {
                _context.City.Add(new City { Name = cleanCityName });
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ManageCities));
            }

            // If it exists, return the view (you can optionally add a ModelState error here)
            ModelState.AddModelError("CityName", "This city already exists.");
            return View();
        }

        /// <summary>
        /// Permanently removes a city from the platform's operational geographic limits.
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
        /// Retrieves a specific city and renders the modification interface.
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
        /// Commits modifications to an existing city entity.
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