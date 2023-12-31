namespace database_api.Models
{
    public class MainResponse
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthenticationResponse? Content { get; set; }
    }
}
