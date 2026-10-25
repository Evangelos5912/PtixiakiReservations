using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PtixiakiReservations.Data;
using PtixiakiReservations.Models;

namespace PtixiakiReservations.Services
{
    public class OrganizerSearchDto
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string UserName { get; set; }
    }

    public class CitySearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class SearchCoordinates
    {
        [System.Text.Json.Serialization.JsonPropertyName("lat")]
        public double Lat { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("lon")]
        public double Lon { get; set; }
    }

    public class VenueSearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CityName { get; set; }
        public SearchCoordinates Location { get; set; }
    }

    public class EventSearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StartDateTime { get; set; }
        public string VenueName { get; set; }
        public string CityName { get; set; }
    }

    public class SearchSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly IElasticSearch _elasticSearch;
        private readonly ILogger<SearchSyncService> _logger;

        public SearchSyncService(ApplicationDbContext context, IElasticSearch elasticSearch, ILogger<SearchSyncService> logger)
        {
            _context = context;
            _elasticSearch = elasticSearch;
            _logger = logger;
        }

        public async Task SyncAllIndicesAsync()
        {
            try
            {
                _logger.LogInformation("Starting full Elasticsearch auto-sync...");

                await SyncCitiesAsync();
                await SyncVenuesAsync();
                await SyncOrganizersAsync();
                await SyncEventsAsync();

                _logger.LogInformation("Full Elasticsearch auto-sync completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during full Elasticsearch auto-sync.");
            }
        }

        public async Task SyncCitiesAsync()
        {
            await _elasticSearch.CreateIndexIfNotExistsAsync("cities");
            var cities = await _context.City
                .Select(c => new CitySearchDto { Id = c.Id.ToString(), Name = c.Name ?? "" })
                .ToListAsync();

            if (cities.Any())
                await _elasticSearch.AddOrUpdateBulkAsync(cities, "cities");
        }

        public async Task SyncVenuesAsync()
        {
            await _elasticSearch.CreateIndexIfNotExistsAsync("venues");
            var venues = await _context.Venue
                .Include(v => v.City)
                .Select(v => new VenueSearchDto 
                { 
                    Id = v.Id.ToString(), 
                    Name = v.Name ?? "", 
                    CityName = v.City != null ? v.City.Name : "" 
                })
                .ToListAsync();

            if (venues.Any())
                await _elasticSearch.AddOrUpdateBulkAsync(venues, "venues");
        }

        public async Task SyncOrganizersAsync()
        {
            await _elasticSearch.CreateIndexIfNotExistsAsync("event_organizers");
            var allowedRoles = new[] { "Admin", "SuperOrganizer", "Event", "Venue", "Event Manager", "Venue Manager", "Super Organizer" };
            
            var users = await _context.Users
                .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id && 
                    _context.Roles.Any(r => allowedRoles.Contains(r.Name) && r.Id == ur.RoleId)))
                .Select(u => new OrganizerSearchDto
                {
                    Id = u.Id,
                    FirstName = u.FirstName ?? "",
                    LastName = u.LastName ?? "",
                    Email = u.Email ?? "",
                    UserName = u.UserName ?? ""
                })
                .ToListAsync();

            if (users.Any())
                await _elasticSearch.AddOrUpdateBulkAsync(users, "event_organizers");
        }

        public async Task SyncEventsAsync()
        {
            await _elasticSearch.CreateIndexIfNotExistsAsync("events");
            var events = await _context.Event
                .Include(e => e.Venue).ThenInclude(v => v.City)
                .Select(e => new EventSearchDto 
                {
                    Id = e.Id.ToString(),
                    Name = e.Name ?? "",
                    StartDateTime = e.StartDateTime.ToString("s"),
                    VenueName = e.Venue != null ? e.Venue.Name : "",
                    CityName = e.Venue != null && e.Venue.City != null ? e.Venue.City.Name : ""
                })
                .ToListAsync();

            if (events.Any())
                await _elasticSearch.AddOrUpdateBulkAsync(events, "events");
        }
    }
}