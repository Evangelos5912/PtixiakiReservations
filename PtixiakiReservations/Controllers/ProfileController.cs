using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Collections.Generic;

namespace PtixiakiReservations.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _env = env;
        }

        // GET: /Profile
        public async Task<IActionResult> Index()
        {
            // Get user with City included
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

        // GET: /Profile/Edit
        public async Task<IActionResult> Edit()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            // Load city name if city exists
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

            return RedirectToAction(nameof(Index));
        }

        // GET: /Profile/Reservations
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

        // GET: /Profile/RequestRoles
        public IActionResult RequestRoles()
        {
            var model = new RoleRequestViewModel 
            { 
                SelectedRoleRequest = "Venue" 
            };
    
            return View(model);
        }

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

            // --- 1. HANDLE PDF UPLOAD ---
            string? savedDocumentPath = null;

            if (pdfDocument != null && pdfDocument.Length > 0)
            {
                // Security Check 1: Ensure it is actually a PDF
                if (pdfDocument.ContentType != "application/pdf" && !pdfDocument.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError("", "Only PDF documents are allowed for verification.");
                    return View(model);
                }

                // Security Check 2: Max 5MB file size to prevent server crashing
                if (pdfDocument.Length > 5242880) 
                {
                    ModelState.AddModelError("", "The PDF document must be smaller than 5MB.");
                    return View(model);
                }

                string uploadsFolder = Path.Combine(_env.ContentRootPath, "SecureDocuments", "RoleRequests");
                
                // Create the folder if it doesn't exist yet
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Create a unique file name so users don't overwrite each other's files
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(pdfDocument.FileName);
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Save the file to the hard drive
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await pdfDocument.CopyToAsync(fileStream);
                }

                savedDocumentPath = "SecureDocuments/RoleRequests/" + uniqueFileName;
            }

            // --- 2. UPDATE USER ROLES & ASSIGN PDF PATH ---
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

            // --- 3. DYNAMIC SUCCESS MESSAGE ---
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

                // 1. Gather all associated structural IDs (Venues & Layouts)
                var venueIds = await _context.Venue.Where(v => v.UserId == userId).Select(v => v.Id).ToListAsync();
                var layoutIds = venueIds.Any() ? await _context.Layout.Where(l => venueIds.Contains(l.VenueId)).Select(l => l.Id).ToListAsync() : new List<int>();

                // 2. Gather all Event IDs (Events the user organized directly OR events happening inside the user's venues/layouts)
                var organizerEventIds = await _context.Event.Where(e => e.OrganizerId == userId).Select(e => e.Id).ToListAsync();
                var venueEventIds = venueIds.Any() ? await _context.Event.Where(e => e.VenueId.HasValue && venueIds.Contains(e.VenueId.Value)).Select(e => e.Id).ToListAsync() : new List<int>();
                
                var masterEventIds = organizerEventIds.Concat(venueEventIds).Distinct().ToList();
                var childEventIds = masterEventIds.Any() ? await _context.Event.Where(e => e.ParentEventId.HasValue && masterEventIds.Contains(e.ParentEventId.Value)).Select(e => e.Id).ToListAsync() : new List<int>();
                
                var allEventIds = masterEventIds.Concat(childEventIds).Distinct().ToList();

                // 3. Harvest physical file paths before destroying DB references
                var eventImagePaths = allEventIds.Any() ? await _context.Event.Where(e => allEventIds.Contains(e.Id)).Select(e => e.ImagePath).ToListAsync() : new List<string>();
                var galleryImagePaths = allEventIds.Any() ? await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).Select(g => g.ImagePath).ToListAsync() : new List<string>();
                var venueImagePaths = venueIds.Any() ? await _context.Venue.Where(v => venueIds.Contains(v.Id)).Select(v => v.imgUrl).ToListAsync() : new List<string>();
                
                var documentPaths = new List<string> 
                { 
                    user.VenueManagerRequestDocumentPath, 
                    user.EventManagerRequestDocumentPath, 
                    user.SuperOrganizerRequestDocumentPath 
                };

                // =======================================================================
                // 4. MEMORY-SAFE BULK DELETIONS (Bottom-Up to prevent FK Restrict crashes)
                // =======================================================================

                // A. Delete Wishlists (User's personal wishlists + ANY wishlists tied to the destroyed events)
                await _context.Set<Wishlist>().Where(w => w.UserId == userId || allEventIds.Contains(w.EventId)).ExecuteDeleteAsync();

                // B. Delete Reservations (User's personal reservations + ANY reservations tied to the destroyed events)
                await _context.Reservation.Where(r => r.UserId == userId || allEventIds.Contains(r.EventId)).ExecuteDeleteAsync();

                // C. Delete Events & Event Images
                if (allEventIds.Any())
                {
                    await _context.Event.Where(e => allEventIds.Contains(e.Id)).SelectMany(e => e.GalleryImages).ExecuteDeleteAsync();
                    if (childEventIds.Any()) await _context.Event.Where(e => childEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                    if (masterEventIds.Any()) await _context.Event.Where(e => masterEventIds.Contains(e.Id)).ExecuteDeleteAsync();
                }

                // D. Delete Layout Items & Layouts
                if (layoutIds.Any())
                {
                    await _context.Seat.Where(s => layoutIds.Contains(s.LayoutId)).ExecuteDeleteAsync();
                    await _context.NonSelectable.Where(ns => layoutIds.Contains(ns.LayoutId)).ExecuteDeleteAsync();
                    await _context.UnitGroup.Where(ug => layoutIds.Contains(ug.LayoutId)).ExecuteDeleteAsync();
                    await _context.Layout.Where(l => layoutIds.Contains(l.Id)).ExecuteDeleteAsync();
                }

                // E. Delete Venue Categories & Venues
                if (venueIds.Any())
                {
                    await _context.VenueCategory.Where(vc => venueIds.Contains(vc.VenueId)).ExecuteDeleteAsync();
                    await _context.Venue.Where(v => venueIds.Contains(v.Id)).ExecuteDeleteAsync();
                }

                // =======================================================================
                // 5. DESTROY IDENTITY AND CLEAR FILES
                // =======================================================================

                // AspNetCore Identity automatically deletes attached Roles/Claims inside this method
                var deleteResult = await _userManager.DeleteAsync(user);
                if (!deleteResult.Succeeded)
                {
                    throw new Exception("Identity system failed to destroy the primary user record.");
                }

                // Only clean up physical disk files if the database transaction fully succeeded
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