using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Seeders;
using PtixiakiReservations.Services;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages core user account operations, including role assignments, security settings, 
    /// elevation requests, and email verification workflows.
    /// </summary>
    public class ApplicationUserController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;

        public ApplicationUserController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signInManager,
            RoleManager<ApplicationRole> roleManager, 
            IEmailService emailService,
            IMemoryCache cache)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _emailService = emailService;
            _cache = cache;
        }

        /// <summary>
        /// Retrieves a paginated and searchable list of all registered users for administrators.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            var query = _context.Users.AsQueryable();

            // Apply search filters.
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(u => 
                    (u.Email != null && u.Email.ToLower().Contains(normalizedQuery)) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(normalizedQuery)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(normalizedQuery)));
            }

            // Calculate pagination metadata.
            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;

            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            // Retrieve paginated records.
            var users = await query
                .OrderBy(u => u.Email) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(); 

            // Populate view context.
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(users);
        }

        /* ==============================================================
         * ROLE ELEVATION REQUEST MANAGEMENT
         * ============================================================== */

        /// <summary>
        /// Retrieves a paginated list of users with pending administrative role requests.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RoleRequests(string searchQuery = null, int pageNumber = 1)
        {
            const int pageSize = 10; 

            // Filter by pending requests.
            var query = _context.Users.Where(u => 
                u.VenueManagerRequestStatus == "Pending" || 
                u.EventManagerRequestStatus == "Pending" || 
                u.SuperOrganizerRequestStatus == "Pending").AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var normalizedQuery = searchQuery.ToLower().Trim();
                query = query.Where(u => 
                    (u.Email != null && u.Email.ToLower().Contains(normalizedQuery)) ||
                    (u.FirstName != null && u.FirstName.ToLower().Contains(normalizedQuery)) ||
                    (u.LastName != null && u.LastName.ToLower().Contains(normalizedQuery)));
            }

            int totalItems = await query.CountAsync();
            int totalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 1;
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var pendingUsers = await query
                .OrderBy(u => u.Email) 
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(); 

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchQuery = searchQuery;
            ViewBag.TotalItems = totalItems;

            return View(pendingUsers);
        }

        /// <summary>
        /// Approves a pending role request and assigns the target role to the user.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveRoleRequest(string userId, string roleType)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roleType)) return BadRequest();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            string identityRoleTarget = "";

            // Map requested role to internal identity role.
            switch (roleType)
            {
                case "VenueManager":
                    user.VenueManagerRequestStatus = "Approved";
                    identityRoleTarget = "Venue Manager"; 
                    break;
                case "Event":
                    user.EventManagerRequestStatus = "Approved";
                    identityRoleTarget = "Event Manager"; 
                    break;
                case "SuperOrganizer":
                    user.SuperOrganizerRequestStatus = "Approved";
                    identityRoleTarget = "Super Organizer";
                    break;
                default:
                    return BadRequest("Invalid role type requested.");
            }

            // Assign the target role.
            if (!string.IsNullOrEmpty(identityRoleTarget))
            {
                if (!await _roleManager.RoleExistsAsync(identityRoleTarget))
                {
                    await _roleManager.CreateAsync(new ApplicationRole { Name = identityRoleTarget });
                }

                if (!await _userManager.IsInRoleAsync(user, identityRoleTarget))
                {
                    await _userManager.AddToRoleAsync(user, identityRoleTarget);
                }
            }

            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(RoleRequests));
        }

        /// <summary>
        /// Rejects a pending role request and updates the user's status.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RejectRoleRequest(string userId, string roleType)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roleType)) return BadRequest();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            switch (roleType)
            {
                case "VenueManager":
                    user.VenueManagerRequestStatus = "Rejected";
                    break;
                case "Event":
                    user.EventManagerRequestStatus = "Rejected";
                    break;
                case "SuperOrganizer":
                    user.SuperOrganizerRequestStatus = "Rejected";
                    break;
            }

            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(RoleRequests));
        }

        /* ==============================================================
         * CORE PROFILE METADATA VIEWS
         * ============================================================== */

        /// <summary>
        /// Retrieves detailed profile information for a specific user.
        /// </summary>
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _context.Users.FirstOrDefaultAsync(m => m.Id == id);
            if (user == null) return NotFound();

            return View(user);
        }

        /// <summary>
        /// Renders the manual role modification interface for administrators.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ChangeRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return View(user);
        }

        /// <summary>
        /// Updates a user's role, removing previous roles to prevent permission overlap.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ChangeRoleAction(string id, string Role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            bool alreadyHasRole = await _userManager.IsInRoleAsync(user, Role);

            // Remove existing roles.
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded) return BadRequest("Failed to remove existing roles.");
            }

            // Toggle role back to standard User if already assigned.
            string roleToAssign = alreadyHasRole ? "User" : Role;

            var addResult = await _userManager.AddToRoleAsync(user, roleToAssign);
            if (!addResult.Succeeded) return BadRequest($"Failed to assign the {roleToAssign} role.");

            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "ApplicationUser");
        }

        /// <summary>
        /// Initializes the baseline administrative account and roles.
        /// </summary>
        public async Task<IActionResult> SeedAdminUser()
        {
            try
            {
                await ApplicationDbSeed.SeedAsync(_userManager, _roleManager);
                return Ok("Admin seeding completed successfully.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Seeding failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Provides autocomplete suggestions for city searches.
        /// </summary>
        [HttpGet]
        public JsonResult SearchCities(string term)
        {
            var cities = _context.City
                .Where(c => c.Name.Contains(term))
                .Select(c => new { id = c.Id, value = c.Name })
                .Take(10)
                .ToList();

            return Json(cities);
        }

        /* ==============================================================
         * AUTHENTICATION & SECURITY OPERATIONS
         * ============================================================== */

        public class Toggle2FaRequest
        {
            public string Password { get; set; }
            public bool Enable { get; set; } 
        }

        /// <summary>
        /// Toggles Two-Factor Authentication (2FA) after verifying the user's password.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle2Fa([FromBody] Toggle2FaRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isPasswordValid) return BadRequest(new { message = "Incorrect password." });

            var result = await _userManager.SetTwoFactorEnabledAsync(user, request.Enable);
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                return Ok(new { message = request.Enable ? "2FA Enabled" : "2FA Disabled" });
            }

            return BadRequest(new { message = "Failed to update settings." });
        }

        /// <summary>
        /// Generates and sends an OTP for initial email verification.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GenerateEmailConfirmationCode()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");

            string subject = "Your EventSphere Security Code";
            string message = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2>Security Verification</h2>
                    <p>You requested a verification code.</p>
                    <p>Your code is: <strong style='font-size: 24px; color: #4F46E5;'>{code}</strong></p>
                    <p style='font-size: 12px; color: #666;'>If you did not request this code, please ignore this email.</p>
                </div>";

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, message);
                return Ok(); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EMAIL ERROR: {ex.Message}");
                return StatusCode(500, new { message = "Failed to send verification email. Please try again." });
            }
        }

        /// <summary>
        /// Validates the email verification OTP and confirms the user's email.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ConfirmEmailCode(string code)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Email", code);

            if (isValid)
            {
                user.EmailConfirmed = true; 
                await _userManager.UpdateAsync(user);
                return Ok(new { success = true });
            }

            return BadRequest("Invalid code.");
        }

        public class EmailChangeRequestDto
        {
            public string NewEmail { get; set; }
        }

        public class ConfirmEmailChangeDto
        {
            public string Code { get; set; }
            public string NewEmail { get; set; }
        }

        /// <summary>
        /// Validates an OTP sent to the user's current email prior to modification.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyCurrentEmailOwnership(string code)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, "Email", code);
            if (!isValid) return BadRequest(new { message = "Invalid or expired code." });

            return Ok();
        }

        /// <summary>
        /// Generates and caches an OTP for the newly requested email address.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateEmailChangeCode([FromBody] EmailChangeRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.NewEmail)) return BadRequest();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var existingUser = await _userManager.FindByEmailAsync(request.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return Ok(); 
            }

            var random = new Random();
            string otpCode = random.Next(100000, 999999).ToString();

            var cacheKey = $"EmailChange_{user.Id}_{request.NewEmail.ToLower()}";
            _cache.Set(cacheKey, otpCode, TimeSpan.FromMinutes(10));

            string subject = "Verify your new email address";
            string message = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                    <h2>Email Change Request</h2>
                    <p>Your verification code to change your email is: <strong style='font-size: 24px; color: #4F46E5;'>{otpCode}</strong></p>
                    <p style='font-size: 12px; color: #666;'>This code expires in 10 minutes.</p>
                </div>";
            
            await _emailService.SendEmailAsync(request.NewEmail, subject, message);

            return Ok();
        }

        /// <summary>
        /// Validates the cached OTP and updates the user's email address.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmEmailChangeCode([FromBody] ConfirmEmailChangeDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.Code) || string.IsNullOrWhiteSpace(request?.NewEmail))
                return BadRequest();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var cacheKey = $"EmailChange_{user.Id}_{request.NewEmail.ToLower()}";
            if (!_cache.TryGetValue(cacheKey, out string expectedCode))
            {
                return BadRequest(new { message = "Code has expired. Please request a new one." });
            }

            if (request.Code != expectedCode)
            {
                return BadRequest(new { message = "Invalid verification code." });
            }

            _cache.Remove(cacheKey);

            user.Email = request.NewEmail;
            user.EmailConfirmed = true; 
            user.UserName = request.NewEmail; 

            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                return Ok();
            }

            return BadRequest(new { message = "Failed to update email in the database." });
        }

        public class ChangePasswordDto
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
            public string ConfirmPassword { get; set; }
        }

        /// <summary>
        /// Updates the user's password and sends a security notification email.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
        {
            if (request.NewPassword != request.ConfirmPassword)
                return BadRequest(new { message = "New passwords do not match." });

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);

                string subject = "Your password has been changed";
                string message = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                        <h2 style='color: #4F46E5;'>Password Changed</h2>
                        <p>Hello,</p>
                        <p>The password for your EventSphere account was recently changed.</p>
                        <p>If you made this change, you can safely ignore this email.</p>
                        <p style='color: #DC2626; font-weight: bold;'>If you did not make this change, please contact support immediately!</p>
                    </div>";
                
                await _emailService.SendEmailAsync(user.Email, subject, message);

                return Ok();
            }

            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }
    }
}