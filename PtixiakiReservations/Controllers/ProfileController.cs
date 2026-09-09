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


namespace PtixiakiReservations.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
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

                // CRITICAL FIX: The string saved to the database MUST match the actual folder!
                // NO LEADING SLASH!
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
    }
}