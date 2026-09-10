﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using PtixiakiReservations.Models.ViewModels;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages administrative and organizational operations for Venues.
    /// Handles physical layout properties, categorical tagging, and file I/O for venue imagery.
    /// </summary>
    public class VenueController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        
        // Note: IHostingEnvironment is maintained for legacy compatibility, 
        // but should be migrated to IWebHostEnvironment in future structural updates.
        [Obsolete] public readonly IHostingEnvironment HostingEnviromnet;

        [Obsolete]
        public VenueController(
            ApplicationDbContext context,
            IHostingEnvironment hostingEnviromnet, 
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
            HostingEnviromnet = hostingEnviromnet;
        }

        /// <summary>
        /// Retrieves a global directory of all venues. 
        /// Restricted to system administrators.
        /// </summary>
        /// <param name="city">Optional geographic filter.</param>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(string city)
        {
            var venuesQuery = _context.Venue.Include(v => v.City).AsQueryable();

            if (!string.IsNullOrWhiteSpace(city))
            {
                venuesQuery = venuesQuery.Where(v => v.City.Name == city);
            }

            return View(await venuesQuery.ToListAsync());
        }

        /// <summary>
        /// Generates a paginated, searchable, and filterable dashboard of venues for organizers.
        /// </summary>
        /// <param name="filter">Contextual scope ('mine' or 'all').</param>
        /// <param name="searchString">Text payload matching against Venue Name or City.</param>
        /// <param name="page">Current pagination index.</param>
        /// <param name="pageSize">Volume constraint per page.</param>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> MyVenues(string filter = "mine", string searchString = null, int page = 1, int pageSize = 12)
        {
            string userId = _userManager.GetUserId(HttpContext.User);
            
            // Preserve state parameters for the frontend UI router
            ViewBag.CurrentFilter = filter;
            ViewBag.SearchString = searchString;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;

            // 1. Initialize eager-loaded query execution sequence
            var query = _context.Venue
                .Include(v => v.City)
                .Include(v => v.VenueCategory)
                    .ThenInclude(vc => vc.EventType)
                .AsQueryable();

            // 2. Apply Ownership Filter
            if (filter != "all") 
            {
                query = query.Where(v => v.UserId == userId);
            }

            // 3. Apply Text-Based Search Filter (Translates to SQL LIKE)
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var normalizedSearch = searchString.ToLower().Trim();
                query = query.Where(v => 
                    v.Name.ToLower().Contains(normalizedSearch) || 
                    v.City.Name.ToLower().Contains(normalizedSearch));
            }

            // 4. Compute aggregate KPIs for the filtered dataset prior to pagination truncation
            int totalCount = await query.CountAsync();
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 1;
            ViewBag.CityCount = await query.Select(v => v.CityId).Distinct().CountAsync();

            // 5. Apply pagination constraints and fetch exact dataset into memory
            var venues = await query
                .OrderBy(v => v.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 6. Aggregate secondary statistics and resolve static file paths
            var layoutCounts = new Dictionary<int, int>();
            var imagePaths = new Dictionary<int, string>();
            
            foreach (var venue in venues)
            {
                layoutCounts[venue.Id] = await _context.Layout.CountAsync(sa => sa.VenueId == venue.Id);
                imagePaths[venue.Id] = GetImagePath(venue.imgUrl);
            }

            ViewBag.LayoutCounts = layoutCounts;
            ViewBag.ImagePaths = imagePaths;

            // Global event cross-reference count based on currently filtered venues
            ViewBag.EventCount = await _context.Event
                .Where(e => query.Any(v => v.Id == e.VenueId))
                .CountAsync();

            return View(venues);
        }

        /// <summary>
        /// Retrieves detailed configuration state for a specific venue modification.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var venue = await _context.Venue
                .Include(v => v.City)
                .Include(v => v.ApplicationUser)
                .Include(v => v.VenueCategory)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venue == null) return NotFound();

            // Security Gate: Ensure users cannot modify venues outside their ownership unless holding Admin privileges
            if (!User.IsInRole("Admin") && _userManager.GetUserId(HttpContext.User) != venue.UserId)
            {
                return Forbid();
            }

            // Extract many-to-many relationship array
            var selectedEventTypeIds = venue.VenueCategory?
                .Where(vc => vc.CategoryId.HasValue)
                .Select(vc => vc.CategoryId.Value)
                .ToList() ?? new List<int>();

            VenueViewModel viewModel = new VenueViewModel
            {
                Id = venue.Id,
                Name = venue.Name,
                Address = venue.Address,
                PostalCode = venue.PostalCode,
                CityId = venue.CityId,
                Phone = venue.Phone,
                VenueUrl = venue.VenueUrl,
                SocialMediaUrl = venue.SocialMediaUrl,
                UserId = venue.UserId,
                SelectedEventTypeIds = selectedEventTypeIds
            };

            PopulateVenueFormLists(venue.CityId, selectedEventTypeIds);
            ViewBag.ImagePath = GetImagePath(venue.imgUrl);

            return View(viewModel);
        }

        /// <summary>
        /// Commits physical metadata and file I/O changes for a specific venue entity.
        /// </summary>
        [HttpPost]
        [Obsolete]
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> Edit(VenueViewModel model)
        {
            if (model == null)
            {
                ViewBag.Error = "Invalid model state submission.";
                return View("Error");
            }

            var venue = await _context.Venue
                .Include(v => v.VenueCategory)
                .FirstOrDefaultAsync(v => v.Id == model.Id);

            if (venue == null) return NotFound();

            if (!User.IsInRole("Admin") && _userManager.GetUserId(HttpContext.User) != venue.UserId)
            {
                return Forbid();
            }

            var selectedEventTypeIds = model.SelectedEventTypeIds?.Distinct().ToList() ?? new List<int>();

            if (ModelState.IsValid)
            {
                string uniqueFileName = null;
                try
                {
                    // Evaluate and process binary file uploads for venue imagery
                    if (model.Photo == null)
                    {
                        uniqueFileName = venue.imgUrl;
                    }
                    else
                    {
                        string uploadsFolder = Path.Combine(HostingEnviromnet.WebRootPath, "images");
                        uniqueFileName = Guid.NewGuid().ToString() + "_" + model.Photo.FileName;
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            model.Photo.CopyTo(fileStream);
                        }
                    }

                    // Apply standard textual metadata modifications
                    venue.Name = model.Name;
                    venue.Phone = model.Phone;
                    venue.PostalCode = model.PostalCode;
                    venue.VenueUrl = model.VenueUrl;
                    venue.SocialMediaUrl = model.SocialMediaUrl;
                    venue.CityId = model.CityId;
                    venue.Address = model.Address;

                    if (uniqueFileName != null)
                    {
                        venue.imgUrl = uniqueFileName;
                    }

                    // Reconstruct many-to-many event category associations
                    if (venue.VenueCategory != null && venue.VenueCategory.Any())
                    {
                        _context.VenueCategory.RemoveRange(venue.VenueCategory);
                    }

                    if (selectedEventTypeIds.Any())
                    {
                        var categoriesToAttach = selectedEventTypeIds.Select(typeId => new VenueCategory
                        {
                            VenueId = venue.Id,
                            CategoryId = typeId
                        }).ToList();

                        _context.VenueCategory.AddRange(categoriesToAttach);
                    }
                    else
                    {
                        // Fallback constraint to ensure venue is universally accessible if not strictly categorized
                        _context.VenueCategory.Add(new VenueCategory
                        {
                            VenueId = venue.Id,
                            CategoryId = null
                        });
                    }

                    _context.Update(venue);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!VenueExists(venue.Id)) return NotFound();
                    throw;
                }

                TempData["SuccessMessage"] = "Venue updated successfully";
                return RedirectToAction(nameof(Details), new { id = venue.Id });
            }
            
            PopulateVenueFormLists(model.CityId, selectedEventTypeIds);
            ViewBag.ImagePath = GetImagePath(venue.imgUrl);
            return View(model);
        }

        /// <summary>
        /// Renders the venue creation interface pre-populated with active geographic options.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public IActionResult Create()
        {
            ViewBag.ListOfCity = _context.City.ToList();
            ViewBag.EventTypes = new MultiSelectList(_context.EventType.ToList(), "Id", "Name");
            
            return View();
        }

        /// <summary>
        /// Processes form payloads to construct and assign a new structural venue identity.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Obsolete]
        public async Task<IActionResult> Create(VenueViewModel model)
        {
            if (ModelState.IsValid)
            {
                var userId = _userManager.GetUserId(User);
                string uniqueFileName = null;

                // Handle binary file I/O operations safely
                if (model.Photo != null)
                {
                    string uploadsFolder = Path.Combine(HostingEnviromnet.WebRootPath, "images");
                    
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    uniqueFileName = Guid.NewGuid().ToString() + "_" + model.Photo.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        model.Photo.CopyTo(fileStream);
                    }
                }

                Venue newVenue = new Venue
                {
                    Name = model.Name,
                    Address = model.Address,
                    CityId = model.CityId,
                    PostalCode = model.PostalCode,
                    Phone = model.Phone,
                    VenueUrl = model.VenueUrl,
                    SocialMediaUrl = model.SocialMediaUrl,
                    UserId = userId,
                    imgUrl = uniqueFileName
                };
                
                _context.Add(newVenue);
                await _context.SaveChangesAsync();

                // Assign complex categorization vectors
                if (model.SelectedEventTypeIds != null && model.SelectedEventTypeIds.Any())
                {
                    var categoriesToAttach = model.SelectedEventTypeIds.Select(typeId => new VenueCategory
                    {
                        VenueId = newVenue.Id,
                        CategoryId = typeId
                    }).ToList();

                    _context.VenueCategory.AddRange(categoriesToAttach);
                }
                else
                {
                    _context.VenueCategory.Add(new VenueCategory
                    {
                        VenueId = newVenue.Id,
                        CategoryId = null 
                    });
                }
                
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Details), new { id = newVenue.Id });
            }

            // Restore dropdown contexts upon validation failure
            ViewBag.ListOfCity = new SelectList(_context.City.ToList(), "Id", "Name", model.CityId);
            ViewBag.EventTypes = new MultiSelectList(_context.EventType.ToList(), "Id", "Name", model.SelectedEventTypeIds);
            
            return View(model);
        }

        /// <summary>
        /// Retrieves the comprehensive public profile of an established venue.
        /// </summary>
        public async Task<IActionResult> Details(int? id)
        {
            if (id is null) return NotFound();

            var venue = await _context.Venue
                .Include(v => v.City)
                .Include(v => v.VenueCategory)           
                    .ThenInclude(vc => vc.EventType)
                .FirstOrDefaultAsync(v => v.Id == id);
        
            if (venue == null) return NotFound();

            if (!User.IsInRole("Admin") && _userManager.GetUserId(HttpContext.User) != venue.UserId)
            {
                return Forbid();
            }

            ViewBag.ImagePath = GetImagePath(venue.imgUrl);
        
            return View(venue);
        }

        /// <summary>
        /// Accesses the destructive deletion interface.
        /// </summary>
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var venue = await _context.Venue.FirstOrDefaultAsync(m => m.Id == id);
            if (venue == null) return NotFound();

            return View(venue);
        }

        /// <summary>
        /// Executes a permanent, cascading structural deletion of a venue entity.
        /// Restricts access to Venue Owners or System Administrators.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Obsolete]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var venue = await _context.Venue.FindAsync(id);
            if (venue == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (!User.IsInRole("Admin") && venue.UserId != currentUserId)
            {
                return Forbid();
            }

            var layouts = await _context.Layout.Where(l => l.VenueId == id).ToListAsync();
            var layoutIds = layouts.Select(l => l.Id).ToList();

            var associatedEvents = await _context.Event
                .Where(e => e.VenueId == id || (e.LayoutId.HasValue && layoutIds.Contains(e.LayoutId.Value)))
                .Include(e => e.GalleryImages) 
                .Include(e => e.ChildEvents)
                    .ThenInclude(c => c.GalleryImages) 
                .ToListAsync();

            if (associatedEvents.Any())
            {
                var allEventIds = new List<int>();
                var allGalleryImages = new List<EventImage>();
                
                foreach (var ev in associatedEvents)
                {
                    allEventIds.Add(ev.Id);

                    if (ev.GalleryImages != null && ev.GalleryImages.Any()) 
                    {
                        allGalleryImages.AddRange(ev.GalleryImages);
                    }

                    if (ev.ChildEvents != null && ev.ChildEvents.Any())
                    {
                        allEventIds.AddRange(ev.ChildEvents.Select(c => c.Id));
                        
                        foreach (var child in ev.ChildEvents)
                        {
                            if (child.GalleryImages != null && child.GalleryImages.Any())
                            {
                                allGalleryImages.AddRange(child.GalleryImages);
                            }
                        }
                    }
                }

                if (allGalleryImages.Any()) 
                {
                    _context.RemoveRange(allGalleryImages);
                }

                // FIX: Delete Wishlist records before deleting Events to prevent RESTRICT FK constraint
                var associatedWishlists = await _context.Set<Wishlist>()
                    .Where(w => allEventIds.Contains(w.EventId))
                    .ToListAsync();

                if (associatedWishlists.Any())
                {
                    _context.Set<Wishlist>().RemoveRange(associatedWishlists);
                }

                var associatedReservations = await _context.Reservation
                    .Where(r => allEventIds.Contains(r.EventId))
                    .ToListAsync();

                if (associatedReservations.Any()) 
                {
                    _context.Reservation.RemoveRange(associatedReservations);
                }
        
                var childEvents = associatedEvents.SelectMany(e => e.ChildEvents ?? new List<Event>()).ToList();
                if (childEvents.Any()) 
                {
                    _context.Event.RemoveRange(childEvents);
                }

                _context.Event.RemoveRange(associatedEvents);
            }

            if (layoutIds.Any())
            {
                var seats = await _context.Seat.Where(s => layoutIds.Contains(s.LayoutId)).ToListAsync();
                if (seats.Any()) _context.Seat.RemoveRange(seats);

                var nonSelectables = await _context.NonSelectable.Where(ns => layoutIds.Contains(ns.LayoutId)).ToListAsync();
                if (nonSelectables.Any()) _context.NonSelectable.RemoveRange(nonSelectables);

                var unitGroups = await _context.UnitGroup.Where(ug => layoutIds.Contains(ug.LayoutId)).ToListAsync();
                if (unitGroups.Any()) _context.UnitGroup.RemoveRange(unitGroups);

                _context.Layout.RemoveRange(layouts);
            }

            var venueCategories = await _context.VenueCategory.Where(vc => vc.VenueId == id).ToListAsync();
            if (venueCategories.Any()) _context.VenueCategory.RemoveRange(venueCategories);

            if (!string.IsNullOrEmpty(venue.imgUrl))
            {
                string imagePath = Path.Combine(HostingEnviromnet.WebRootPath, "images", venue.imgUrl.TrimStart('/', '\\'));
                if (System.IO.File.Exists(imagePath)) System.IO.File.Delete(imagePath);
            }

            _context.Venue.Remove(venue);

            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(MyVenues));
        }
        
        /// <summary>
        /// Exposes a JSON payload of venues scoped entirely to the actively authenticated user.
        /// Commonly consumed by asynchronous frontend interfaces (AJAX).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public IActionResult GetVenuesForUser()
        {
            string userId = _userManager.GetUserId(HttpContext.User);
            var venues = _context.Venue
                .Where(v => v.UserId == userId)
                .Select(v => new { id = v.Id, name = v.Name })
                .ToList();
    
            return Json(venues);
        }

        /// <summary>
        /// Validates existential bounds within the current datastore.
        /// </summary>
        private bool VenueExists(int id)
        {
            return _context.Venue.Any(e => e.Id == id);
        }

        /// <summary>
        /// Populates contextual SelectLists required by form ViewModels.
        /// </summary>
        private void PopulateVenueFormLists(int selectedCityId, IEnumerable<int> selectedEventTypeIds = null)
        {
            ViewBag.ListOfCity = new SelectList(_context.City.ToList(), "Id", "Name", selectedCityId);
            ViewBag.EventTypes = new MultiSelectList(
                _context.EventType.ToList(),
                "Id",
                "Name",
                selectedEventTypeIds ?? Enumerable.Empty<int>());
        }
    
        /// <summary>
        /// Validates physical existence of an image payload, providing a static fallback upon resolution failure.
        /// </summary>
        private string GetImagePath(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return "~/images/image.jpg";
            
            var imagePath = Path.Combine(HostingEnviromnet.WebRootPath, "images", imageUrl);
            
            if (System.IO.File.Exists(imagePath)) return $"~/images/{imageUrl}";
            
            return "~/images/image.jpg";
        }
    }
}