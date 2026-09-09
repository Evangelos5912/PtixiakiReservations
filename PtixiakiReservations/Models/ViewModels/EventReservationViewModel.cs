// Models/ViewModels/EventReservationViewModel.cs
using System;

namespace PtixiakiReservations.Models.ViewModels
{
    public class EventReservationViewModel
    {
        public int ReservationId { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public DateTime ReservationDate { get; set; }
        public int SeatCount { get; set; }
        public decimal TotalPaid { get; set; }
        public string SeatName { get; set; } 
    }
}