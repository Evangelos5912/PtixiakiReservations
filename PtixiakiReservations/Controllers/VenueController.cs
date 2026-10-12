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
using PtixiakiReservations.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Webp;

namespace PtixiakiReservations.Controllers
{
    /// <summary>
    /// Manages venue operations, layouts, categorical tagging, imagery, and search synchronization.
    /// </summary>
    public class VenueController : Controller
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly IElasticSearch _elasticSearch;
        
        [Obsolete] public readonly IHostingEnvironment HostingEnviromnet;

        [Obsolete]
        public VenueController(
            IWebHostEnvironment environment,
            ApplicationDbContext context,
            IHostingEnvironment hostingEnviromnet, 
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            IElasticSearch elasticSearch)
        {
            _environment = environment;
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
            HostingEnviromnet = hostingEnviromnet;
            _elasticSearch = elasticSearch;
        }

        public class VenueSearchDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string CityName { get; set; }
        }

        private async Task SyncVenueToElasticAsync(int venueId)
        {
            var venue = await _context.Venue
                .Include(v => v.City)
                .FirstOrDefaultAsync(v => v.Id == venueId);

            if (venue != null)
            {
                var dto = new VenueSearchDto
                {
                    Id = venue.Id.ToString(),
                    Name = venue.Name ?? "",
                    CityName = venue.City != null ? venue.City.Name : ""
                };
                await _elasticSearch.CreateIndexIfNotExistsAsync("venues");
                await _elasticSearch.AddOrUpdateAsync(dto, "venues");
            }
        }

        private async Task RemoveVenueFromElasticAsync(int venueId)
        {
            await _elasticSearch.DeleteByIdAsync<VenueSearchDto>(venueId.ToString(), "venues");
        }

        /// <summary>
        /// Retrieves the global directory of all venues.
        /// </summary>
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
        /// Retrieves paginated venues for organizers.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> MyVenues(string filter = "mine", string searchString = null, int page = 1, int pageSize = 12)
        {
            string userId = _userManager.GetUserId(HttpContext.User);
            
            ViewBag.CurrentFilter = filter;
            ViewBag.SearchString = searchString;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;

            var query = _context.Venue
                .Include(v => v.City)
                .Include(v => v.VenueCategory)
                    .ThenInclude(vc => vc.EventType)
                .AsQueryable();

            if (filter != "all") 
            {
                query = query.Where(v => v.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var normalizedSearch = searchString.ToLower().Trim();
                query = query.Where(v => 
                    v.Name.ToLower().Contains(normalizedSearch) || 
                    v.City.Name.ToLower().Contains(normalizedSearch));
            }

            int totalCount = await query.CountAsync();
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 1;
            ViewBag.CityCount = await query.Select(v => v.CityId).Distinct().CountAsync();

            var venues = await query
                .OrderBy(v => v.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var layoutCounts = new Dictionary<int, int>();
            var imagePaths = new Dictionary<int, string>();
            
            foreach (var venue in venues)
            {
                layoutCounts[venue.Id] = await _context.Layout.CountAsync(sa => sa.VenueId == venue.Id);
                
                if (!string.IsNullOrEmpty(venue.imgUrl))
                {
                    imagePaths[venue.Id] = venue.imgUrl.EndsWith(".webp") 
                        ? (venue.imgUrl.StartsWith("/") ? venue.imgUrl : "/" + venue.imgUrl.TrimStart('/')) 
                        : $"/Events/GetCompressedImage?path={venue.imgUrl}&width=800";
                }
                else
                {
                    imagePaths[venue.Id] = "/images/image.jpg";
                }
            }

            ViewBag.LayoutCounts = layoutCounts;
            ViewBag.ImagePaths = imagePaths;

            ViewBag.EventCount = await _context.Event
                .Where(e => query.Any(v => v.Id == e.VenueId))
                .CountAsync();

            return View(venues);
        }

        /// <summary>
        /// Retrieves the venue modification view.
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

            if (!User.IsInRole("Admin") && _userManager.GetUserId(HttpContext.User) != venue.UserId)
            {
                return Forbid();
            }

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
        /// Processes venue updates and synchronizes search indices.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> Edit(VenueViewModel model)
        {
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
                string uniqueFileName = venue.imgUrl;

                try
                {
                    if (model.Photo != null && model.Photo.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(venue.imgUrl))
                        {
                            string oldFilePath = Path.Combine(_environment.WebRootPath, venue.imgUrl.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);
                        }

                        string uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "venues");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        uniqueFileName = "/images/venues/" + Guid.NewGuid().ToString() + ".webp";
                        string filePath = Path.Combine(_environment.WebRootPath, uniqueFileName.TrimStart('/'));

                        using (var image = await SixLabors.ImageSharp.Image.LoadAsync(model.Photo.OpenReadStream()))
                        {
                            if (image.Width > 1920)
                            {
                                int newHeight = (int)((double)image.Height / image.Width * 1920);
                                image.Mutate(x => x.Resize(1920, newHeight));
                            }

                            var encoder = new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = 80 };
                            await image.SaveAsync(filePath, encoder);
                        }
                    }

                    venue.Name = model.Name;
                    venue.Phone = model.Phone;
                    venue.PostalCode = model.PostalCode;
                    venue.VenueUrl = model.VenueUrl;
                    venue.SocialMediaUrl = model.SocialMediaUrl;
                    venue.CityId = model.CityId;
                    venue.Address = model.Address;
                    venue.imgUrl = uniqueFileName;

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
                        _context.VenueCategory.Add(new VenueCategory
                        {
                            VenueId = venue.Id,
                            CategoryId = null
                        });
                    }

                    _context.Update(venue);
                    await _context.SaveChangesAsync();
                    await SyncVenueToElasticAsync(venue.Id);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Error processing image data: " + ex.Message);
                    PopulateVenueFormLists(model.CityId, selectedEventTypeIds);
                    ViewBag.ImagePath = GetImagePath(venue.imgUrl);
                    return View(model);
                }

                TempData["SuccessMessage"] = "Venue updated successfully";
                return RedirectToAction(nameof(Details), new { id = venue.Id });
            }
            
            PopulateVenueFormLists(model.CityId, selectedEventTypeIds);
            ViewBag.ImagePath = GetImagePath(venue.imgUrl);
            return View(model);
        }

        /// <summary>
        /// Renders the venue creation view.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public IActionResult Create()
        {
            ViewBag.ListOfCity = _context.City.ToList();
            ViewBag.EventTypes = new MultiSelectList(_context.EventType.ToList(), "Id", "Name");
            
            return View();
        }

        /// <summary>
        /// Processes venue creation and synchronizes search indices.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(VenueViewModel model)
        {
            if (ModelState.IsValid)
            {
                var userId = _userManager.GetUserId(User);
                string uniqueFileName = null;

                if (model.Photo != null && model.Photo.Length > 0)
                {
                    try
                    {
                        string uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "venues");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        uniqueFileName = "/images/venues/" + Guid.NewGuid().ToString() + ".webp";
                        string filePath = Path.Combine(_environment.WebRootPath, uniqueFileName.TrimStart('/'));

                        using (var image = await SixLabors.ImageSharp.Image.LoadAsync(model.Photo.OpenReadStream()))
                        {
                            if (image.Width > 1920)
                            {
                                int newHeight = (int)((double)image.Height / image.Width * 1920);
                                image.Mutate(x => x.Resize(1920, newHeight));
                            }

                            var encoder = new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = 80 };
                            await image.SaveAsync(filePath, encoder);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", "Error processing image data: " + ex.Message);
                        ViewBag.ListOfCity = _context.City.ToList();
                        ViewBag.EventTypes = new MultiSelectList(_context.EventType.ToList(), "Id", "Name", model.SelectedEventTypeIds);
                        return View(model);
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
                await SyncVenueToElasticAsync(newVenue.Id);

                return RedirectToAction(nameof(Details), new { id = newVenue.Id });
            }

            ViewBag.ListOfCity = _context.City.ToList();
            ViewBag.EventTypes = new MultiSelectList(_context.EventType.ToList(), "Id", "Name", model.SelectedEventTypeIds);
            
            return View(model);
        }

        /// <summary>
        /// Retrieves the public profile of a venue.
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

            string detailImagePath = "/images/image.jpg";
            if (!string.IsNullOrEmpty(venue.imgUrl))
            {
                detailImagePath = venue.imgUrl.EndsWith(".webp") 
                    ? (venue.imgUrl.StartsWith("/") ? venue.imgUrl : "/" + venue.imgUrl.TrimStart('/')) 
                    : $"/Events/GetCompressedImage?path={venue.imgUrl}&width=1000";
            }

            ViewBag.ImagePath = detailImagePath;
        
            return View(venue);
        }

        /// <summary>
        /// Renders the venue deletion view.
        /// </summary>
        [Authorize(Roles = "Admin,Venue,SuperOrganizer")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var venue = await _context.Venue.FirstOrDefaultAsync(m => m.Id == id);
            if (venue == null) return NotFound();

            return View(venue);
        }

        /// <summary>
        /// Removes a venue and synchronizes search indices.
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

            await RemoveVenueFromElasticAsync(id);
            
            return RedirectToAction(nameof(MyVenues));
        }
        
        /// <summary>
        /// Retrieves venues scoped to the authenticated user.
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

        private bool VenueExists(int id)
        {
            return _context.Venue.Any(e => e.Id == id);
        }

        private void PopulateVenueFormLists(int selectedCityId, IEnumerable<int> selectedEventTypeIds = null)
        {
            ViewBag.ListOfCity = new SelectList(_context.City.ToList(), "Id", "Name", selectedCityId);
            ViewBag.EventTypes = new MultiSelectList(
                _context.EventType.ToList(),
                "Id",
                "Name",
                selectedEventTypeIds ?? Enumerable.Empty<int>());
        }
    
        private string GetImagePath(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return "~/images/image.jpg";
            
            var imagePath = Path.Combine(HostingEnviromnet.WebRootPath, "images", imageUrl);
            
            if (System.IO.File.Exists(imagePath)) return $"~/images/{imageUrl}";
            
            return "~/images/image.jpg";
        }
    }
}