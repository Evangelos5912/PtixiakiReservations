using System.Collections.Generic;

namespace PtixiakiReservations.Models
{
    public class UnitGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Top { get; set; }
        public decimal Left { get; set; }
        public int LayoutId { get; set; }
        public Layout Layout { get; set; }

        public ICollection<Seat> SelectableUnits { get; set; } = new List<Seat>();
        public ICollection<NonSelectable> NonSelectableUnits { get; set; } = new List<NonSelectable>();
    }
}