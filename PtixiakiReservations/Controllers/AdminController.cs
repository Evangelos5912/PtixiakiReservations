using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting; 
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace PtixiakiReservations.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IWebHostEnvironment env) 
        {
            _userManager = userManager;
            _context = context;
            _env = env;
        }

        // Retrieves metrics and pending requests for the administrative dashboard.
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

        // Retrieves paginated and filtered pending role authorization requests.
        public async Task<IActionResult> RoleRequests(string? searchQuery, int pageNumber = 1)
        {
            int pageSize = 12; 

            var query = _context.Users
                .Where(u => 
                    (u.HasRequestedVenueManagerRole && u.VenueManagerRequestStatus == "Pending") ||
                    (u.HasRequestedEventManagerRole && u.EventManagerRequestStatus == "Pending") ||
                    (u.HasRequestedSuperOrganizerRole && u.SuperOrganizerRequestStatus == "Pending")
                );

            // Apply search filters.
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string searchLower = searchQuery.ToLower();
                query = query.Where(u => 
                    u.FirstName.ToLower().Contains(searchLower) || 
                    u.LastName.ToLower().Contains(searchLower) || 
                    u.Email.ToLower().Contains(searchLower));
            }

            // Calculate pagination parameters.
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var pendingRequests = await query
                .OrderByDescending(u => u.VenueManagerRequestDate ?? u.EventManagerRequestDate ?? u.SuperOrganizerRequestDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Populate view context data.
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages == 0 ? 1 : totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(pendingRequests);
        }

        // Approves a pending role request for the specified user and updates their role.
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

            // Purge associated verification documents from the file system.
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
                
                // Return status 200 OK for AJAX clients.
                return Ok(); 
            }
            
            return BadRequest("Could not approve the request.");
        }

        // Rejects a pending role request for the specified user.
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

            // Purge associated verification documents from the file system.
            DeleteVerificationDocument(user, roleType);
            
            if (wasRejected)
            {
                await _userManager.UpdateAsync(user);
                return Ok(); 
            }

            return BadRequest("Could not reject the request.");
        }

        // Retrieves and serves the PDF verification document for authorized administrators.
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

            // Resolve the absolute file path securely across platforms.
            string absolutePath = ResolveAbsolutePath(docPath);

            // Serve the file directly to the client.
            if (System.IO.File.Exists(absolutePath))
            {
                return PhysicalFile(absolutePath, "application/pdf");
            }

            return NotFound($"File is missing from the server. Looked for it at: {absolutePath}");
        }

        // --- Helper Methods ---

        // Removes the physical document and clears the reference in the database.
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

            // Clear the database property tracking the path.
            switch (roleType)
            {
                case "Venue": user.VenueManagerRequestDocumentPath = null; break;
                case "Event": user.EventManagerRequestDocumentPath = null; break;
                case "SuperOrganizer": user.SuperOrganizerRequestDocumentPath = null; break;
            }
        }

        // Safely resolves the absolute file path regardless of operating system.
        private string ResolveAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            // Extract the filename safely, ignoring any stored directory structure.
            string fileName = Path.GetFileName(path);

            // Construct the path referencing the secure application directory.
            string securePath = Path.Combine(_env.ContentRootPath, "SecureDocuments", "RoleRequests", fileName);

            if (System.IO.File.Exists(securePath))
            {
                return securePath;
            }

            // Fallback strategy for legacy uploads stored within the public web root.
            if (!string.IsNullOrEmpty(_env.WebRootPath))
            {
                string legacyPath = Path.Combine(_env.WebRootPath, "uploads", "verification_docs", fileName);
                if (System.IO.File.Exists(legacyPath))
                {
                    return legacyPath;
                }
            }

            // Return the target path to expose resolution errors during diagnostics.
            return securePath;
        }
    }
}