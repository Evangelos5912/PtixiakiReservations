using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;
using PtixiakiReservations.Services;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Collections.Generic;

namespace PtixiakiReservations.Controllers
{
    // Handles user profile management, role requests, and account deletion
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IElasticSearch _elasticSearch;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context,
            IWebHostEnvironment env,
            IElasticSearch elasticSearch)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _env = env;
            _elasticSearch = elasticSearch;
        }

        // DTO for Elasticsearch organizer indexing
        public class OrganizerSearchDto
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
            public string UserName { get; set; }
        }

        // Syncs eligible organizers to Elasticsearch or removes them
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

        // Retrieves and displays the user's profile dashboard and recent reservations
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var user = await _context.Users
                .Include(u => u.City)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var reservations = await _context.Reservation
                .Include(r => r.Event)
                .Include(r => r.Seat)
                .Include(r => r.Seat.Layout)
                .Include(r => r.Seat.Layout.Venue)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.Date)
                .Take(5)
                .ToListAsync();

            var model = new ProfileViewModel
            {
                User = user,
                Roles = roles,
                RecentReservations = reservations,

                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                PostalCode = user.PostalCode,
                CityId = user.CityId,
                IsEmailConfirmed = user.EmailConfirmed,

                Is2faEnabled = user.TwoFactorEnabled 
            };

            return View(model);
        }

        // Renders the profile edit form
        public async Task<IActionResult> Edit()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            if (user.CityId.HasValue)
            {
                var city = await _context.City.FindAsync(user.CityId.Value);
                ViewBag.CityName = city?.Name;
            }

            var model = new ProfileEditViewModel
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                CityId = user.CityId,
                Address = user.Address,
                PostalCode = user.PostalCode
            };

            return View(model);
        }

        // Updates user profile information and syncs changes to search index
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ProfileEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.PhoneNumber = model.PhoneNumber;
            user.CityId = model.CityId;
            user.Address = model.Address;
            user.PostalCode = model.PostalCode;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            await SyncOrRemoveUserFromElasticAsync(user);
            return RedirectToAction(nameof(Index));
        }

        // Retrieves all reservations for the current user
        public async Task<IActionResult> Reservations()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var reservations = await _context.Reservation
                .Include(r => r.Event)
                .Include(r => r.Seat)
                .Include(r => r.Seat.Layout)
                .Include(r => r.Seat.Layout.Venue)
                .Where(r => r.UserId == user.Id)
                .OrderByDescending(r => r.Date)
                .ToListAsync();

            return View(reservations);
        }

        // Renders the role request form
        public IActionResult RequestRoles()
        {
            var model = new RoleRequestViewModel 
            { 
                SelectedRoleRequest = "Venue" 
            };
    
            return View(model);
        }

        // Processes role elevation requests and securely stores verification documents
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRoles(RoleRequestViewModel model, IFormFile? pdfDocument)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            string? savedDocumentPath = null;

            if (pdfDocument != null && pdfDocument.Length > 0)
            {
                if (pdfDocument.ContentType != "application/pdf" && !pdfDocument.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError("", "Only PDF documents are allowed for verification.");
                    return View(model);
                }

                if (pdfDocument.Length > 5242880) 
                {
                    ModelState.AddModelError("", "The PDF document must be smaller than 5MB.");
                    return View(model);
                }

                string uploadsFolder = Path.Combine(_env.ContentRootPath, "SecureDocuments", "RoleRequests");
                
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(pdfDocument.FileName);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfDocument.CopyToAsync(fileStream);
                }

                savedDocumentPath = "SecureDocuments/RoleRequests/" + uniqueFileName;
            }

            switch (model.SelectedRoleRequest)
            {
                case "Venue":
                    user.HasRequestedVenueManagerRole = true;
                    user.VenueManagerRequestStatus = "Pending";
                    user.VenueManagerRequestDate = DateTime.UtcNow;
                    user.VenueManagerRequestReason = model.Reason;
                    if (savedDocumentPath != null) user.VenueManagerRequestDocumentPath = savedDocumentPath;
                    break;
                    
                case "Event":
                    user.HasRequestedEventManagerRole = true;
                    user.EventManagerRequestStatus = "Pending";
                    user.EventManagerRequestDate = DateTime.UtcNow;
                    user.EventManagerRequestReason = model.Reason;
                    if (savedDocumentPath != null) user.EventManagerRequestDocumentPath = savedDocumentPath;
                    break;
                    
                case "SuperOrganizer":
                    user.HasRequestedSuperOrganizerRole = true;
                    user.SuperOrganizerRequestStatus = "Pending";
                    user.SuperOrganizerRequestDate = DateTime.UtcNow;
                    user.SuperOrganizerRequestReason = model.Reason;
                    if (savedDocumentPath != null) user.SuperOrganizerRequestDocumentPath = savedDocumentPath;
                    break;
                    
                default:
                    ModelState.AddModelError("", "Invalid role selected.");
                    return View(model);
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }

            string displayRole = model.SelectedRoleRequest switch
            {
                "Venue" => "Venue Manager",
                "Event" => "Event Organizer",
                "SuperOrganizer" => "Super Organizer",
                _ => "requested role"
            };

            TempData["SuccessMessage"] = $"Your request to become a {displayRole} has been submitted and is pending approval.";
            return RedirectToAction(nameof(Index));
        }

        // Permanently deletes the user account and cascades deletion of all associated data
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAccount()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, message = "User account not found." });
            }

            try
            {
                var userId = user.Id;

                var venueIds = await _context.Venue.Where(v => v.UserId == userId).Select(v => v.Id).ToListAsync();
                var layoutIds = venueIds.Any() ? await _context.Layout.Where(l => venueIds.Contains(l.VenueId)).Select(l => l.Id).ToListAsync() : new List<int>();

                var organizerEventIds = await _context.Event.Where(e => e.OrganizerId == userId).Select(e => e.Id).ToListAsync();
                var venueEventIds = venueIds.Any() ? await _context.Event.Where(e => e.VenueId.HasValue && venueIds.Contains(e.VenueId.Value)).Select(e => e.Id).ToListAsync() : new List<int>();
                
                var masterEventIds = organizerEventIds.Concat(venueEventIds).Distinct().ToList();
                var childEventIds = masterEventIds.Any() ? await _context.Event.Where(e => e.ParentEventId.HasValue && masterEventIds.Contains(e.ParentEventId.Value)).Select(e => e.Id).ToListAsync() : new List<int>();
                
                var allEventIds = masterEventIds.Concat(childEventIds).Distinct().ToList();

                var eventImagePaths = allEventIds.Any() ? await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync() : new List<string>();
                var galleryImagePaths = allEventIds.Any() ? await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync() : new List<string>();
                var venueImagePaths = venueIds.Any() ? await _context.Venue.Where(v => venueIds.Contains(v.Id)).Select(v => v.imgUrl).ToListAsync() : new List<string>();
                
                var documentPaths = new List<string> 
                { 
                    user.VenueManagerRequestDocumentPath, 
                    user.EventManagerRequestDocumentPath, 
                    user.SuperOrganizerRequestDocumentPath 
                };

                await _context.Set<Wishlist>().Where(w => w.UserId == userId || allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();
                await _context.Reservation.Where(r => r.UserId == userId || allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();

                if (allEventIds.Any())
                {
                    await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();
                    if (childEventIds.Any()) await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    if (masterEventIds.Any()) await _context.Event.Where(e => masterEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                }

                if (layoutIds.Any())
                {
                    await _context.Seat.Where(s => layoutIds.Contains(s.LayoutId)).ExecuteDeleteAsync();
                    await _context.NonSelectable.Where(ns => layoutIds.Contains(ns.LayoutId)).ExecuteDeleteAsync();
                    await _context.UnitGroup.Where(ug => layoutIds.Contains(ug.LayoutId)).ExecuteDeleteAsync();
                    await _context.Layout.Where(l => layoutIds.Contains(l.Id)).ExecuteDeleteAsync();
                }

                if (venueIds.Any())
                {
                    await _context.VenueCategory.Where(vc => venueIds.Contains(vc.VenueId)).ExecuteDeleteAsync();
                    await _context.Venue.Where(v => venueIds.Contains(v.Id)).ExecuteDeleteAsync();
                }

                var deleteResult = await _userManager.DeleteAsync(user);
                if (!deleteResult.Succeeded)
                {
                    throw new Exception("Identity system failed to destroy the primary user record.");
                }

                await _elasticSearch.DeleteByIdAsync<OrganizerSearchDto>(userId, "event_organizers");

                var imagePathsToDelete = eventImagePaths.Concat(galleryImagePaths).Where(p => !string.IsNullOrEmpty(p)).ToList();
                foreach (var path in imagePathsToDelete)
                {
                    var fullPath = Path.Combine(_env.WebRootPath, path.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }

                foreach (var path in venueImagePaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, "images", path.TrimStart('/', '\\'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }

                foreach (var docPath in documentPaths.Where(p => !string.IsNullOrEmpty(p)))
                {
                    var fullPath = Path.Combine(_env.ContentRootPath, docPath);
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }

                await _signInManager.SignOutAsync();
                
                return Json(new { success = true, redirectUrl = "/" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "System exception encountered during deletion: " + ex.Message });
            }
        }
    }
}