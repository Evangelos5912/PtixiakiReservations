using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PtixiakiReservations.Models; 

namespace PtixiakiReservations.Seeders;

public static class RoleSeeder
{
    public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        string[] roleNames = { "Admin", "User", "SuperOrganizer", "Event", "Venue" };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    description = $"{roleName} role",
                    creationDate = DateTime.UtcNow
                });
            }
        }

        // Must use .com to pass standard ASP.NET Core [EmailAddress] validation
        string adminEmail = "admin@admin.com"; 
        var adminUser = await userManager.FindByEmailAsync(adminEmail);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Admin",
                LastName = "User",
                EmailConfirmed = true
            };

            // This will only work if Program.cs has Identity password requirements relaxed
            var result = await userManager.CreateAsync(adminUser, "123456");

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
            else 
            {
                Console.WriteLine($"ADMIN CREATION FAILED: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}