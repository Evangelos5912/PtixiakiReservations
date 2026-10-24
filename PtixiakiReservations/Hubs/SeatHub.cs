using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PtixiakiReservations.Hubs
{
    // Manages real-time seat locking and synchronization using SignalR
    public class SeatHub : Hub
    {
        // Tracks active seat locks organized by layout ID and seat ID
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<int, string>> _activeLocks = new();

        // Subscribes a client to a layout group and broadcasts existing locks
        public async Task JoinLayoutGroup(int layoutId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"layout_{layoutId}");

            if (_activeLocks.TryGetValue(layoutId, out var layoutLocks))
            {
                var lockedSeatIds = layoutLocks.Keys.ToList();
                if (lockedSeatIds.Any())
                {
                    await Clients.Caller.SendAsync("InitializeLocks", lockedSeatIds);
                }
            }
        }

        // Unsubscribes a client and releases their active locks
        public async Task LeaveLayoutGroup(int layoutId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"layout_{layoutId}");
            await CleanupUserLocks(Context.ConnectionId);
        }

        // Claims a temporary lock on a specific seat and notifies other clients
        public async Task LockSeat(int layoutId, int seatId)
        {
            var layoutLocks = _activeLocks.GetOrAdd(layoutId, _ => new ConcurrentDictionary<int, string>());
            
            if (layoutLocks.TryAdd(seatId, Context.ConnectionId))
            {
                await Clients.OthersInGroup($"layout_{layoutId}").SendAsync("SeatLocked", seatId, Context.ConnectionId);
            }
        }

        // Releases a seat lock if owned by the requester and notifies others
        public async Task UnlockSeat(int layoutId, int seatId)
        {
            if (_activeLocks.TryGetValue(layoutId, out var layoutLocks))
            {
                if (layoutLocks.TryGetValue(seatId, out var ownerId) && ownerId == Context.ConnectionId)
                {
                    layoutLocks.TryRemove(seatId, out _);
                    await Clients.OthersInGroup($"layout_{layoutId}").SendAsync("SeatUnlocked", seatId, Context.ConnectionId);
                }
            }
        }

        // Automatically clears orphan locks upon client disconnection
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await CleanupUserLocks(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        // Iterates and removes all locks held by a specific connection ID
        private async Task CleanupUserLocks(string connectionId)
        {
            foreach (var layoutKvp in _activeLocks)
            {
                var layoutId = layoutKvp.Key;
                var layoutLocks = layoutKvp.Value;

                var seatsToUnlock = layoutLocks.Where(x => x.Value == connectionId).Select(x => x.Key).ToList();

                foreach (var seatId in seatsToUnlock)
                {
                    layoutLocks.TryRemove(seatId, out _);
                    await Clients.Group($"layout_{layoutId}").SendAsync("SeatUnlocked", seatId, connectionId);
                }
            }
        }
    }
}