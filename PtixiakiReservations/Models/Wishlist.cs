using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;


namespace PtixiakiReservations.Models;

public class Wishlist
{
    public int Id { get; set; }
    public string UserId { get; set; }
    [ForeignKey("UserId")] public ApplicationUser ApplicationUser { get; set; }
    public int EventId { get; set; }
    [ForeignKey("EventId")] public Event Event { get; set;}
}