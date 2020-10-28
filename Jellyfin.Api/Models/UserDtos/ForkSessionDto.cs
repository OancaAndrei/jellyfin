using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Api.Models.UserDtos
{
    /// <summary>
    /// The fork session request body.
    /// </summary>
    public class ForkSessionDto
    {
        /// <summary>
        /// Gets or sets the access token.
        /// </summary>
        [Required]
        public string? AccessToken { get; set; }

        /// <summary>
        /// Gets or sets the device identifier.
        /// </summary>
        [Required]
        public string? DeviceId { get; set; }
    }
}
