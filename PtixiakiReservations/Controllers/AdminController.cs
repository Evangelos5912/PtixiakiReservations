using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting; 
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Services;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Handles admin dashboard, user role approvals, and search indexing.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IElasticSearch _elasticSearch;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IWebHostEnvironment env,
            IElasticSearch elasticSearch) 
        {
            _userManager = userManager;
            _context = context;
            _env = env;
            _elasticSearch = elasticSearch;
        }

        public class OrganizerSearchDto
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
            public string UserName { get; set; }
        }

        // Updates or removes user from ElasticSearch based on active organizer roles.
        private async Task SyncOrRemoveUserFromElasticAsync(ApplicationUser user)
        {
            string indexName = "event_organizers";
            var allowedRoles = new[] { "Admin", "SuperOrganizer", "Event", "Venue", "Event Manager", "Venue Manager", "Super Organizer" };
            
            bool isOrganizer = false;
            foreach (var role in allowedRoles)
            {
                if (await _userManager.IsInRoleAsync(user, role))
                {
                    isOrganizer = true;
                    break;
                }
            }

            if (isOrganizer)
            {
                var dto = new OrganizerSearchDto
                {
                    Id = user.Id,
                    FirstName = user.FirstName ?? "",
                    LastName = user.LastName ?? "",
                    Email = user.Email ?? "",
                    UserName = user.UserName ?? ""
                };
                await _elasticSearch.CreateIndexIfNotExistsAsync(indexName);
                await _elasticSearch.AddOrUpdateAsync(dto, indexName);
            }
            else
            {
                await _elasticSearch.DeleteByIdAsync<OrganizerSearchDto>(user.Id, indexName);
            }
        }

        /// <summary>
        /// Loads admin dashboard statistics.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var model = new
            {
                VenueCount = await _context.Venue.CountAsync(),
                EventCount = await _context.Event.CountAsync(),
                LayoutCount = await _context.Layout.CountAsync(),
                ReservationCount = await _context.Reservation.CountAsync(),
                PendingRequests = await _context.Users
                    .Where(u => (u.HasRequestedVenueManagerRole && u.VenueManagerRequestStatus == "Pending") ||
                        (u.HasRequestedEventManagerRole && u.EventManagerRequestStatus == "Pending")||
                        (u.HasRequestedSuperOrganizerRole && u.SuperOrganizerRequestStatus == "Pending"))
                    .CountAsync()
            };

            return View(model);
        }

        /// <summary>
        /// Paginates and filters pending role requests.
        /// </summary>
        public async Task<IActionResult> RoleRequests(string? searchQuery, int pageNumber = 1)
        {
            int pageSize = 12; 

            var query = _context.Users
                .Where(u => 
                    (u.HasRequestedVenueManagerRole && u.VenueManagerRequestStatus == "Pending") ||
                    (u.HasRequestedEventManagerRole && u.EventManagerRequestStatus == "Pending") ||
                    (u.HasRequestedSuperOrganizerRole && u.SuperOrganizerRequestStatus == "Pending")
                );

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string searchLower = searchQuery.ToLower();
                query = query.Where(u => 
                    u.FirstName.ToLower().Contains(searchLower) || 
                    u.LastName.ToLower().Contains(searchLower) || 
                    u.Email.ToLower().Contains(searchLower));
            }

            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var pendingRequests = await query
                .OrderByDescending(u => u.VenueManagerRequestDate ?? u.EventManagerRequestDate ?? u.SuperOrganizerRequestDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages == 0 ? 1 : totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(pendingRequests);
        }

        /// <summary>
        /// Approves a role request, assigns the role, and updates the search index.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRoleRequest(string userId, string roleType)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            bool wasApproved = false;
            string newRole = string.Empty;

            switch (roleType)
            {
                case "Venue":
                    if (user.HasRequestedVenueManagerRole && user.VenueManagerRequestStatus == "Pending")
                    {
                        user.VenueManagerRequestStatus = "Approved";
                        newRole = "Venue";
                        wasApproved = true;
                    }
                    break;

                case "Event":
                    if (user.HasRequestedEventManagerRole && user.EventManagerRequestStatus == "Pending")
                    {
                        user.EventManagerRequestStatus = "Approved";
                        newRole = "Event";
                        wasApproved = true;
                    }
                    break;

                case "SuperOrganizer":
                    if (user.HasRequestedSuperOrganizerRole && user.SuperOrganizerRequestStatus == "Pending")
                    {
                        user.SuperOrganizerRequestStatus = "Approved";
                        newRole = "SuperOrganizer";
                        wasApproved = true;
                    }
                    break;

                default:
                    return BadRequest("Invalid role type submitted.");
            }

            DeleteVerificationDocument(user, roleType);

            if (wasApproved && !string.IsNullOrEmpty(newRole))
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                if (currentRoles.Any())
                {
                    await _userManager.RemoveFromRolesAsync(user, currentRoles);
                }

                await _userManager.AddToRoleAsync(user, newRole);
                await _userManager.UpdateAsync(user);
                
                await SyncOrRemoveUserFromElasticAsync(user);

                return Ok(); 
            }
            
            return BadRequest("Could not approve the request.");
        }

        /// <summary>
        /// Rejects a role request and updates the search index.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRoleRequest(string userId, string roleType)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            bool wasRejected = false;

            switch (roleType)
            {
                case "Venue":
                    if (user.HasRequestedVenueManagerRole && user.VenueManagerRequestStatus == "Pending")
                    {
                        user.VenueManagerRequestStatus = "Rejected";
                        wasRejected = true;
                    }
                    break;

                case "Event":
                    if (user.HasRequestedEventManagerRole && user.EventManagerRequestStatus == "Pending")
                    {
                        user.EventManagerRequestStatus = "Rejected";
                        wasRejected = true;
                    }
                    break;

                case "SuperOrganizer":
                    if (user.HasRequestedSuperOrganizerRole && user.SuperOrganizerRequestStatus == "Pending")
                    {
                        user.SuperOrganizerRequestStatus = "Rejected";
                        wasRejected = true;
                    }
                    break;

                default:
                    return BadRequest("Invalid role type submitted.");
            }

            DeleteVerificationDocument(user, roleType);
            
            if (wasRejected)
            {
                await _userManager.UpdateAsync(user);
                await SyncOrRemoveUserFromElasticAsync(user);
                return Ok(); 
            }

            return BadRequest("Could not reject the request.");
        }

        /// <summary>
        /// Serves the user's verification PDF securely.
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> ViewVerificationDocument(string userId, string roleType)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            string? docPath = roleType switch
            {
                "Venue" => user.VenueManagerRequestDocumentPath,
                "Event" => user.EventManagerRequestDocumentPath,
                "SuperOrganizer" => user.SuperOrganizerRequestDocumentPath,
                _ => null
            };

            if (string.IsNullOrEmpty(docPath)) return NotFound("No document found in the database.");

            string absolutePath = ResolveAbsolutePath(docPath);

            if (System.IO.File.Exists(absolutePath))
            {
                return PhysicalFile(absolutePath, "application/pdf");
            }

            return NotFound($"File is missing from the server.");
        }

        // Deletes the associated verification file from the server.
        private void DeleteVerificationDocument(ApplicationUser user, string roleType)
        {
            string? docPathToDelete = roleType switch
            {
                "Venue" => user.VenueManagerRequestDocumentPath,
                "Event" => user.EventManagerRequestDocumentPath,
                "SuperOrganizer" => user.SuperOrganizerRequestDocumentPath,
                _ => null
            };

            if (!string.IsNullOrEmpty(docPathToDelete))
            {
                string absolutePath = ResolveAbsolutePath(docPathToDelete);
                if (System.IO.File.Exists(absolutePath))
                {
                    System.IO.File.Delete(absolutePath);
                }
            }

            switch (roleType)
            {
                case "Venue": user.VenueManagerRequestDocumentPath = null; break;
                case "Event": user.EventManagerRequestDocumentPath = null; break;
                case "SuperOrganizer": user.SuperOrganizerRequestDocumentPath = null; break;
            }
        }

        // Locates the document path across secure and legacy directories.
        private string ResolveAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            string fileName = Path.GetFileName(path);
            string securePath = Path.Combine(_env.ContentRootPath, "SecureDocuments", "RoleRequests", fileName);

            if (System.IO.File.Exists(securePath))
            {
                return securePath;
            }

            if (!string.IsNullOrEmpty(_env.WebRootPath))
            {
                string legacyPath = Path.Combine(_env.WebRootPath, "uploads", "verification_docs", fileName);
                if (System.IO.File.Exists(legacyPath))
                {
                    return legacyPath;
                }
            }

            return securePath;
        }
    }
}