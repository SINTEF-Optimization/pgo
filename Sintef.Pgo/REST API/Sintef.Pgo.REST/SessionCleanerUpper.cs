using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST
{
	/// <summary>
	/// A background service that periodically calls <see cref="IMultiUserServer.CleanUpSessions"/>
	/// </summary>
	public class SessionCleanerUpper : BackgroundService
	{
		/// <summary>
		/// The interval of time that passes between each cleanup
		/// </summary>
		public TimeSpan Interval
		{
			get => _interval; 
			set
			{
				_interval = value;
				_intervalChanged?.Cancel();
			}
		}

		/// <summary>
		/// The server to clean up
		/// </summary>
		private IMultiUserServer _server;

		/// <summary>
		/// The interval between cleanups
		/// </summary>
		private TimeSpan _interval = TimeSpan.FromSeconds(60);

		/// <summary>
		/// Provides a cancellation token that is cancelled when the Iterval is changed
		/// </summary>
		private CancellationTokenSource _intervalChanged;

		/// <summary>
		/// Initializes the cleanerupper
		/// </summary>
		/// <param name="server">The server to clean up</param>
		public SessionCleanerUpper(IMultiUserServer server)
		{
			_server = server;
		}

		/// <summary>
		/// The main function that periodically cleans up
		/// </summary>
		/// <param name="stoppingToken">The function returns when this token is cancelled</param>
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (true)
			{
				_intervalChanged = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

				// Wait until the interval has passed (or been changed) (or the whole service is cancelled)
				try
				{
					await Task.Delay(Interval, _intervalChanged.Token);
				}
				catch (TaskCanceledException) { }

				if (stoppingToken.IsCancellationRequested)
					return;

				_server.CleanUpSessions();
			}
		}
	}
}