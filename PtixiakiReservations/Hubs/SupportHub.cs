using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;



namespace EventSphere.Hubs
{
    public class SupportHub : Hub
    {
        // User requests an admin
        public async Task RequestAdmin(string userName)
        {
            // Send the request to all connected admins
            await Clients.Group("Admins").SendAsync("ReceiveSupportRequest", Context.ConnectionId, userName);
        }

        // Admin joins a specific user's chat room
        public async Task AdminAcceptsRequest(string userConnectionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, userConnectionId); // Admin joins user's private group
            await Clients.Client(userConnectionId).SendAsync("AdminJoined", Context.ConnectionId);
        }

        // Send a message between user and admin
        public async Task SendMessage(string targetConnectionId, string message, bool isAdmin)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveLiveMessage", message, isAdmin);
        }

        // Admin closes the ticket
        public async Task CloseTicket(string userConnectionId)
        {
            await Clients.Client(userConnectionId).SendAsync("TicketClosed");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userConnectionId);
        }

        // When an admin connects, add them to the Admins group
        public async Task RegisterAdmin()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
        }

        // When a user cancels their request before an admin answers
        public async Task CancelRequest()
        {
            await Clients.Group("Admins").SendAsync("RemoveSupportRequest", Context.ConnectionId);
        }

        // When a user presses Reset/Restart during an active live chat
        public async Task UserEndsChat(string adminConnectionId)
        {
            await Clients.Client(adminConnectionId).SendAsync("UserLeftChat");
        }
    }
}