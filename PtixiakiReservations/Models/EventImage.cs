using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace PtixiakiReservations.Models;

public class EventImage
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } 
    
    public Event? Event { get; set; }
}