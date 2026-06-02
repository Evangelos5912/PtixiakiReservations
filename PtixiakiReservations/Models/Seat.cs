using System.ComponentModel.DataAnnotations.Schema;

namespace PtixiakiReservations.Models;

public class Seat
{
    public int Id { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public string Name { get; set; }
    public bool Available { get; set; }
    public int SubAreaId { get; set; }
    [ForeignKey("SubAreaId")] public SubArea SubArea { get; set; }
    
    // Dimensions for resizable square tables
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
}