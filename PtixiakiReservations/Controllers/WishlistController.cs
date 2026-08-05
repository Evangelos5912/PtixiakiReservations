using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;
using System.Linq;
using System.Threading.Tasks;

namespace PtixiakiReservations.Controllers
{
    public class WishlistController : Controller
    {        private readonly ApplicationDbContext _context;

        public WishlistController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> returnItem(string userId, int eventId)
        {
            var wishlistItem = await _context.Wishlist
            .AnyAsync(w => w.UserId == userId && w.EventId == eventId);

            return Json(wishlistItem);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> addWishlistEvent(string userId, int eventId)
        {
            if (!string.IsNullOrWhiteSpace(userId) && eventId != null)
            {
                _context.Wishlist.Add(new Wishlist {UserId = userId, EventId = eventId});
                await _context.SaveChangesAsync();
                return Json(true);}
            return Json(false);
        }

        [HttpDelete]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> deleteWishlistEvent(string userId, int eventId)
        {
            var wishlistItem = await _context.Wishlist
            .FirstOrDefaultAsync(w => w.UserId == userId && w.EventId == eventId);



            if (wishlistItem != null)
            {
                _context.Wishlist.Remove(wishlistItem);
                await _context.SaveChangesAsync();
                return Json(true);

            }
            return Json(false);
        }


    }

    
}