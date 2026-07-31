using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace PtixiakiReservations.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EventTypeController : Controller
    {       private readonly ApplicationDbContext _context;
            private readonly IWebHostEnvironment _environment;
            private readonly ILogger<EventTypeController> _logger;

        public EventTypeController(IWebHostEnvironment environment, ILogger<EventTypeController> logger, ApplicationDbContext context)
        {
            _environment = environment;
            _logger = logger;
            _context = context;
        }

        //GET: return all event types
        public async Task<IActionResult> ManageEventType()
        {
            var et = await _context.EventType.ToListAsync();
            return View(et);
        }

        public IActionResult CreateEventType()
        {
            return View();
        }

        //POST: creatin event type
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEventType(string ETName, IFormFile? imageFile)
        {
            if (string.IsNullOrWhiteSpace(ETName))
            {
                ModelState.AddModelError("ETName", "Event Type Name is required.");
                return View();
            }

            string imagePath = null;
            if (imageFile != null && imageFile.Length > 0)
            {
                try
                {
                    string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(fileStream);
                    }

                    imagePath = "/images/eventTypes/" + uniqueFileName;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving event type image to disk.");
                    return BadRequest(new { success = false, message = "Error saving image." });
                }
            }
            
            _context.EventType.Add(new EventType {Name = ETName, ImagePath = imagePath});
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageEventType));
        }

        //POST: delete event type
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEventType(int id)
        {
            var et = await _context.EventType.FindAsync(id);

            if (et != null)
            {
                _context.EventType.Remove(et);
                await _context.SaveChangesAsync();

            }
            return RedirectToAction(nameof(ManageEventType));
        }

        //GET: return selected event type to edit
        public async Task<IActionResult> EditEventType(int ETId)
        {
            var et = await _context.EventType.FindAsync(ETId);

            if (et == null)
            {
                return NotFound();

            }

            return View(et);
        }

        //POST: edit event type
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEventType(int ETId, string ETName, IFormFile? imageFile)
        {

            var et = await _context.EventType.FindAsync(ETId);

            if(et!=null){


                if (string.IsNullOrWhiteSpace(ETName))
                {
                    ModelState.AddModelError("ETName", "Event Type Name is required.");
                    return View();
                }
                else
                {
                    et.Name = ETName;
                }
                

                
                if (imageFile != null && imageFile.Length > 0)
                {
                    try
                    {
                        string uploadsFolder = Path.Combine(_environment.WebRootPath, "images/eventTypes");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        et.ImagePath = "/images/eventTypes/" + uniqueFileName;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error saving event type image to disk.");
                        return BadRequest(new { success = false, message = "Error saving image." });
                    }
                }

             
            
                _context.Update(et);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ManageEventType));
            }
            else
            {
                return NoContent();
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.EventType
                .Select(c => new { id = c.Id, name = c.Name, imagePath = c.ImagePath})
                .ToListAsync();

            return Json(categories);
        }

    }
    }