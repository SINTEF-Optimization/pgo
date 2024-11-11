using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Sintef.Pgo.REST.Extensions;

namespace Sintef.Pgo.REST.Hubs
{
	/// <summary>
	/// A hub for communicating the solution status to subscribed clients.
	/// </summary>
	/// <remarks>
	/// https://docs.microsoft.com/en-us/archive/msdn-magazine/2018/april/cutting-edge-discovering-asp-net-core-signalr
	/// Also https://docs.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-2.2
	/// </remarks>
	public class SolutionStatusHub : Hub
	{
		#region External hub methods, to be called from clients

		/// <summary>
		/// Join the group that gets the status for a specific session
		/// </summary>
		/// <param name="sessionId">The id of the session to join.</param>
		/// <returns></returns>
		public async Task AddToGroup(string sessionId)
		{
			await Groups.AddToGroupAsync(Context.ConnectionId, GroupId(UserId, sessionId));
		}

		/// <summary>
		/// Leave the group that gets the status for a specific session
		/// </summary>
		/// <param name="sessionId">The id of the session to join.</param>
		/// <returns></returns>
		public async Task RemoveFromGroup(string sessionId)
		{
			await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupId(UserId, sessionId));
		}

		#endregion

		/// <summary>
		/// Returns a proxy for sending messages to all connections associated with a
		/// user and a session
		/// </summary>
		internal static IClientProxy Receivers(IHubContext<SolutionStatusHub> solutionStatusHubContext, string userId, string sessionId)
		{
			return solutionStatusHubContext.Clients.Group(GroupId(userId, sessionId));
		}

		/// <summary>
		/// The ID of the user associcated with the current request
		/// </summary>
		private string UserId => Context.User.GetUserId();

		/// <summary>
		/// Returns the group ID to use for the given user ID and session ID
		/// </summary>
		private static string GroupId(string userId, string sessionId)
		{
			return $"{userId.Length}/{userId}/{sessionId}";
		}
	}
}
