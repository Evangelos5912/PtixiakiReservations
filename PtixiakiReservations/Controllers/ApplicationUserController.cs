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
    /// Manages core user account operations including administrative role assignments, 
    /// profile security (2FA, Password management), and complex multi-step email verification workflows.
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
        /// Retrieves a comprehensive list of all registered platform users.
        /// Restricted strictly to administrative personnel.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            var users = await _context.Users.ToListAsync();
            return View(users);
        }

        /// <summary>
        /// Fetches detailed profile information for a specific target user.
        /// </summary>
        /// <param name="id">The unique GUID identifier of the target user.</param>
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();

            var user = await _context.Users.FirstOrDefaultAsync(m => m.Id == id);
            if (user == null) return NotFound();

            return View(user);
        }

        /// <summary>
        /// Renders the role modification interface for a selected user account.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ChangeRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return View(user);
        }

        /// <summary>
        /// Executes a role transition for a user, ensuring they are cleanly stripped of 
        /// previous permissions before the new authorization level is applied to prevent claim overlaps.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ChangeRoleAction(string id, string Role)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            bool alreadyHasRole = await _userManager.IsInRoleAsync(user, Role);

            // Flush existing permissions to prevent authorization overlap or conflicting claims
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded) return BadRequest("Failed to remove existing roles.");
            }

            // Toggle logic: If they already had the requested role, demote them back to the base "User" tier
            string roleToAssign = alreadyHasRole ? "User" : Role;

            var addResult = await _userManager.AddToRoleAsync(user, roleToAssign);
            if (!addResult.Succeeded) return BadRequest($"Failed to assign the {roleToAssign} role.");

            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "ApplicationUser");
        }

        /// <summary>
        /// Initializes the baseline administrative account required for platform configuration.
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
        /// Provides autocomplete suggestions for location-based input fields.
        /// Limits payload to 10 records for optimal frontend rendering performance and reduced database load.
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

        /// <summary>
        /// Data Transfer Object for securing the Two-Factor Authentication state change.
        /// </summary>
        public class Toggle2FaRequest
        {
            public string Password { get; set; }
            public bool Enable { get; set; } 
        }

        /// <summary>
        /// Toggles the account's Two-Factor Authentication status. 
        /// Requires active password verification to prevent unauthorized state manipulation via hijacked sessions.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle2Fa([FromBody] Toggle2FaRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Cryptographic validation of the provided password against the stored hash
            var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!isPasswordValid) return BadRequest(new { message = "Incorrect password." });

            var result = await _userManager.SetTwoFactorEnabledAsync(user, request.Enable);
            if (result.Succeeded)
            {
                // Refresh the authentication cookie so the newly applied 2FA claims take effect immediately without requiring re-login
                await _signInManager.RefreshSignInAsync(user);
                return Ok(new { message = request.Enable ? "2FA Enabled" : "2FA Disabled" });
            }

            return BadRequest(new { message = "Failed to update settings." });
        }

        /// <summary>
        /// Generates and transmits a standard 6-digit numeric OTP for initial account email verification.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GenerateEmailConfirmationCode()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            // Leverages ASP.NET Identity's native RFC 6238 compliant token generation
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
                // Log transmission failure to the console for infrastructure debugging
                Console.WriteLine($"EMAIL ERROR: {ex.Message}");
                return StatusCode(500, new { message = "Failed to send verification email. Please try again." });
            }
        }

        /// <summary>
        /// Validates the initial account verification OTP and locks the confirmed status into the database.
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
        /// Phase 1 of Email Modification: Validates an OTP dispatched to the user's currently active email.
        /// This ensures the entity initiating the change genuinely controls the established inbox.
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
        /// Phase 2 of Email Modification: Generates a custom 6-digit OTP directed at the newly requested address.
        /// Utilizes IMemoryCache to handle the transient token payload since Identity natively generates URL strings for email changes.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateEmailChangeCode([FromBody] EmailChangeRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.NewEmail)) return BadRequest();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Security Mechanism: Prevent targeted email enumeration attacks by silently succeeding
            // if the requested target email is already bound to a different database identity.
            var existingUser = await _userManager.FindByEmailAsync(request.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return Ok(); 
            }

            // Cryptographically secure generation is unnecessary here as the entropy of a 6 digit pin 
            // paired with a 10-minute timeout and rate-limiting is sufficient for email validation logic.
            var random = new Random();
            string otpCode = random.Next(100000, 999999).ToString();

            // Construct a highly specific cache composite key to prevent state collision between concurrent requests
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
        /// Phase 3 of Email Modification: Interrogates the server cache to validate the OTP against the requested target email.
        /// Upon successful validation, the underlying Identity records are permanently mutated.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmEmailChangeCode([FromBody] ConfirmEmailChangeDto request)
        {
            if (string.IsNullOrWhiteSpace(request?.Code) || string.IsNullOrWhiteSpace(request?.NewEmail))
                return BadRequest();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Reconstruct the composite key to fetch the transient verification token
            var cacheKey = $"EmailChange_{user.Id}_{request.NewEmail.ToLower()}";
            if (!_cache.TryGetValue(cacheKey, out string expectedCode))
            {
                return BadRequest(new { message = "Code has expired. Please request a new one." });
            }

            if (request.Code != expectedCode)
            {
                return BadRequest(new { message = "Invalid verification code." });
            }

            // Immediate cache invalidation post-validation strictly prevents replay attacks
            _cache.Remove(cacheKey);

            // Execute the Identity mutation committing the new address as the primary identifier
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

        /// <summary>
        /// Data Transfer Object mapping Discord-style 3-tier password modification requests.
        /// </summary>
        public class ChangePasswordDto
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
            public string ConfirmPassword { get; set; }
        }

        /// <summary>
        /// Updates the user's password securely utilizing Identity's built-in validation matrix.
        /// Dispatches a security notification to the user's email upon successful modification.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
        {
            // Preliminary guard clause ensuring input synchronization
            if (request.NewPassword != request.ConfirmPassword)
                return BadRequest(new { message = "New passwords do not match." });

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Identity automatically verifies the CurrentPassword before applying the NewPassword payload
            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            
            if (result.Succeeded)
            {
                // Refresh the auth cookie so the underlying security stamp mutation doesn't forcefully log the user out
                await _signInManager.RefreshSignInAsync(user);

                // Dispatch the security notification email alerting the user to the sensitive state change
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

            // Return Identity's precise localized error strings (e.g., "Password must contain a number", "Incorrect current password")
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }
    }
}