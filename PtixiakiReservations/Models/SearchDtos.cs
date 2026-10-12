namespace PtixiakiReservations.Models
{
    public class CitySearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class VenueSearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CityName { get; set; }
    }

    public class EventSearchDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StartDateTime { get; set; }
        public string VenueName { get; set; }
        public string CityName { get; set; }
    }
}