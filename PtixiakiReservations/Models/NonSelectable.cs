using System.ComponentModel.DataAnnotations.Schema;

namespace PtixiakiReservations.Models;

public class NonSelectable
{
public int Id { get; set; }
    public int SubAreaId { get; set; } 
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; } 
    public decimal Height { get; set; } 
    public string ShapeType { get; set; } 
    public string BackgroundColor { get; set; } = "#E5E7EB"; 
    public string Name { get; set; }
    public bool Scene { get; set; } 
    [ForeignKey("SubAreaId")] 
    public SubArea SubArea { get; set; }
}