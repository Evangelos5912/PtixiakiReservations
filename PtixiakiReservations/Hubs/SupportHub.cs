using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace EventSphere.Hubs
{
    // Manages real-time live support routing between users and administrators
    public class SupportHub : Hub
    {
        // Broadcasts a live support request to all connected administrators
        public async Task RequestAdmin(string userName)
        {
            await Clients.Group("Admins").SendAsync("ReceiveSupportRequest", Context.ConnectionId, userName);
        }

        // Subscribes an administrator to a user's private chat group
        public async Task AdminAcceptsRequest(string userConnectionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userConnectionId); 
            await Clients.Client(userConnectionId).SendAsync("AdminJoined", Context.ConnectionId);
        }

        // Routes a direct message between a user and an administrator
        public async Task SendMessage(string targetConnectionId, string message, bool isAdmin)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveLiveMessage", message, isAdmin);
        }

        // Terminates the active support session and notifies the user
        public async Task CloseTicket(string userConnectionId)
        {
            await Clients.Client(userConnectionId).SendAsync("TicketClosed");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userConnectionId);
        }

        // Subscribes the connecting administrator to the global admin broadcast group
        public async Task RegisterAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        // Retracts a pending support request from the administrator queue
        public async Task CancelRequest()
        {
            await Clients.Group("Admins").SendAsync("RemoveSupportRequest", Context.ConnectionId);
        }

        // Notifies the administrator that the user has terminated the session
        public async Task UserEndsChat(string adminConnectionId)
        {
            await Clients.Client(adminConnectionId).SendAsync("UserLeftChat");
        }
    }
}