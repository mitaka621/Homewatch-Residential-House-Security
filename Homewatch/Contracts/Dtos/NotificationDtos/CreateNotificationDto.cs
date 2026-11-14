using Homewatch.Contracts.Constants;
using Homewatch.Contracts.Enums;
using MudBlazor;

namespace Homewatch.Contracts.Dtos.NotificationDtos
{
    public class CreateNotificationDto
    {
        public required string Name { get; set; }

        public required string Description { get; set; }

        public Color IconColor { get; set; }

        public string Icon { get; set; } = NotificationDefaults.Icon;

        public string? RelativeRedirectUrl { get; set; }

        public NotificationType NotificationType { get; set; }

        //unique notifications will not be sent again if the same notification is already sent before and is still undismissed
        public bool IsUnique { get; set; }
    }
}
