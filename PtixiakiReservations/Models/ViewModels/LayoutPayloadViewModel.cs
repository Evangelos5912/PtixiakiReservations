using System.Collections.Generic;

namespace PtixiakiReservations.Models.ViewModels
{
    public class LayoutPayloadViewModel
    {
        public List<SeatViewModel> Seats { get; set; } = new List<SeatViewModel>();
        public List<ShapeViewModel> Shapes { get; set; } = new List<ShapeViewModel>();
        public List<GroupViewModel> Groups { get; set; } = new List<GroupViewModel>();
    }

    public class GroupViewModel
    {
        public string Name { get; set; }
        public decimal Top { get; set; }
        public decimal Left { get; set; }
        public int LayoutId { get; set; }
        public List<SeatViewModel> SelectableUnits { get; set; } = new List<SeatViewModel>();
        public List<ShapeViewModel> NonSelectableUnits { get; set; } = new List<ShapeViewModel>();
    }

    public class SeatViewModel
    {
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public int LayoutId { get; set; }
        public bool Available { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
    }

    public class ShapeViewModel
    {
        public string Name { get; set; }
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public int LayoutId { get; set; }
        public string ShapeType { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
    }
}