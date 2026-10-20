using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PtixiakiReservations.Hubs
{
    public class SeatHub : Hub
    {
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<int, string>> _activeLocks = new();

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

        public async Task LeaveLayoutGroup(int layoutId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"layout_{layoutId}");
            await CleanupUserLocks(Context.ConnectionId);
        }

        public async Task LockSeat(int layoutId, int seatId)
        {
            var layoutLocks = _activeLocks.GetOrAdd(layoutId, _ => new ConcurrentDictionary<int, string>());
            
            if (layoutLocks.TryAdd(seatId, Context.ConnectionId))
            {
                await Clients.OthersInGroup($"layout_{layoutId}").SendAsync("SeatLocked", seatId, Context.ConnectionId);
            }
        }

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

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await CleanupUserLocks(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

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