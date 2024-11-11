using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using System;
using System.Net;
using Xunit.Abstractions;
using System.Collections.Generic;
using Sintef.Pgo.REST.Client;
using System.Threading;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for per-user quotas
	/// </summary>
	public class UserQuotaTests : LiveServerFixture
	{
		private NetworkBuilder _builder = NetworkBuilder.Create("Gen1[generator] -- Line2 -- Node3[consumer]");
		private NetworkBuilder _builder2 = NetworkBuilder.Create(
			"Gen1[generator] -- Line2 -- Node3[consumer]",
			"Gen1 -- Line4 -- Node5[consumer]");

		public UserQuotaTests(ITestOutputHelper output) : base(output)
		{
			Reinitialize(multiUser: true);
		}

		[Fact]
		public void ServerReportsQuotas()
		{
			CreateDefaultClient();

			UserQuotas quotas = Client.GetQuotas();
			UserQuotas expected = UserQuotas.NoLimits();

			// These are the default (unlimited) quotas
			Assert.Equal(expected, quotas);
		}

		[Fact]
		public void ServerCanUseDemoQuotas()
		{
			Reinitialize(demoQuotas: true);
			CreateDefaultClient();

			UserQuotas quotas = Client.GetQuotas();
			UserQuotas expected = UserQuotas.ForDemo();

			// These are the demo quotas
			Assert.Equal(expected, quotas);
		}

		[Fact]
		public void QuotasCanBeOverriddenThroughBackdoor()
		{
			CreateDefaultClient();

			UserQuotas quotas = CreateClient("user2", overrideDefaultQuotas: q => q with
			{
				NetworkLimit = 1,
				SessionLimit = 1,
				OptimizationTimeout = TimeSpan.FromHours(6),
				SessionTimeout = TimeSpan.FromHours(6),
				NetworkMaxSize = 1003
			}
			).GetQuotas();

			Assert.Equal(1, quotas.NetworkLimit);
			Assert.Equal(1, quotas.SessionLimit);
			Assert.Equal(TimeSpan.FromHours(6), quotas.OptimizationTimeout);
			Assert.Equal(TimeSpan.FromHours(6), quotas.SessionTimeout);
			Assert.Equal(1003, quotas.NetworkMaxSize);
		}

		//[Fact]
		//public void BadClaimsAreIgnored()
		//{
		//	SetupServer();

		//	UserQuotas quotas = CreateClient("user2",
		//		(_networkLimitClaim, "abc"), // Bad int
		//		(_sessionLimitClaim, "1"),
		//		(_optimizationTimeoutClaim, "PT6H")
		//	).GetQuotas();

		//	Assert.Equal(2, quotas.NetworkLimit); // Default
		//	Assert.Equal(1, quotas.SessionLimit);
		//	Assert.Equal(TimeSpan.FromHours(6), quotas.OptimizationTimeout);


		//	quotas = CreateClient("user3",
		//		(_sessionLimitClaim, "1"),
		//		(_optimizationTimeoutClaim, "PT6H"),
		//		(_sessionTimeoutClaim, "-----") // Bad time span
		//	).GetQuotas();

		//	Assert.Equal(1, quotas.SessionLimit);
		//	Assert.Equal(TimeSpan.FromHours(6), quotas.OptimizationTimeout);
		//	Assert.Equal(TimeSpan.FromHours(24), quotas.SessionTimeout); // Default
		//}

		[Fact]
		public void NetworkQuotaWorks()
		{
			CreateDefaultClient();

			var client = CreateClient("user2", overrideDefaultQuotas: q => q with { NetworkLimit = 1 });

			client.LoadNetworkFromBuilder("net1", _builder);

			// Adding second network fails
			AssertException(() => client.LoadNetworkFromBuilder("net2", _builder),
				requiredMessage: "This user is limited to 1 networks at the same time", requiredCode: HttpStatusCode.Forbidden);

			// If we remove the first network, it succeeds
			client.DeleteNetwork("net1");
			client.LoadNetworkFromBuilder("net2", _builder);
		}

		[Fact]
		public void NetworkSizeQuotaWorks()
		{
			CreateDefaultClient();

			var client = CreateClient("user2", overrideDefaultQuotas: q => q with { NetworkMaxSize = 2 });

			// This network has 2 buses, so OK
			client.LoadNetworkFromBuilder("net1", _builder);
			client.DeleteNetwork("net1");

			// This network has 3 buses
			AssertException(() => client.LoadNetworkFromBuilder("net2", _builder2),
				requiredMessage: "This user is limited to networks with a maximum of 2 nodes", requiredCode: HttpStatusCode.Forbidden);
		}

		[Fact]
		public void SessionQuotaWorks()
		{
			CreateDefaultClient();

			var client = CreateClient("user2", overrideDefaultQuotas: q => q with { SessionLimit = 1 });

			client.LoadNetworkFromBuilder(NetworkId, _builder);
			client.CreateJsonSession("sess1", NetworkId, _builder);

			// Adding second session fails
			AssertException(() => client.CreateJsonSession("sess2", NetworkId, _builder),
				requiredMessage: "This user is limited to 1 sessions at the same time", requiredCode: HttpStatusCode.Forbidden);

			// If we remove the first session, it succeeds
			client.DeleteSession("sess1");
			client.CreateJsonSession("sess2", NetworkId, _builder);
		}

		[Fact]
		public void OptimizationTimeoutQuotaWorks()
		{
			var client = CreateClient("user", overrideDefaultQuotas: q => q with { OptimizationTimeout = TimeSpan.FromSeconds(0.5) });

			SetupSession(_builder, client);

			Task stopped = Stopped(client, SessionId);

			client.StartOptimizing(SessionId);

			// Wait for optimization to stop (with timeout)
			stopped.Wait(timeout: TimeSpan.FromSeconds(1));

			Assert.True(stopped.IsCompleted);
		}

		[Fact]
		public void SessionTimesOutAfterCreation()
		{
			VerifySessionTimeout((client) => { });
		}

		[Fact]
		public void SessionTimesOutAfterOptimizationStop()
		{
			VerifySessionTimeout((client) =>
			{
				client.StartOptimizing(SessionId);

				// While optimizing, the session does not time out
				Thread.Sleep(400);

				client.StopOptimizing(SessionId);
			});
		}

		[Fact]
		public void SessionTimesOutAfterOptimizationTimeout()
		{
			VerifySessionTimeout((client) =>
			{
				client.StartOptimizing(SessionId);

				// Wait for optimization to time out
				Stopped(client, SessionId).Wait();
			}, timeOutOptimization: true);
		}

		[Fact]
		public void SessionTimesOutAfterSolutionAdded()
		{
			VerifySessionTimeout((client) =>
			{
				var solution = AllEqualSettings(_builder, open: true);

				Thread.Sleep(100);

				client.AddSolution(SessionId, "sol", solution);
			});
		}

		[Fact]
		public void SessionTimesOutAfterSolutionRemoved()
		{
			VerifySessionTimeout((client) =>
			{
				var solution = AllEqualSettings(_builder, open: true);
				client.AddSolution(SessionId, "sol", solution);

				Thread.Sleep(150);

				client.RemoveSolution(SessionId, "sol");
			});
		}

		/// <summary>
		/// Verifies that a session is removed after the correct timeout (set to 200ms)
		/// </summary>
		/// <param name="delayTimeout">An action that should delay the session's timeout. The timeout
		///   should start when the action finishes.</param>
		/// <param name="timeOutOptimization">If true, optimization is given a 200ms timeout</param>
		private void VerifySessionTimeout(Action<PgoRestClient> delayTimeout, bool timeOutOptimization = false)
		{
			TestUtils.RepeatTest(5, () =>
			{
				// Run cleanup often to enable quick tests
				SetCleanupInterval(TimeSpan.FromMilliseconds(10));

				// Set up a client with short timeouts
				UserQuotas configureQuotas(UserQuotas q)
				{
					q = q with { SessionTimeout = TimeSpan.FromSeconds(0.2) };
					if (timeOutOptimization)
						q = q with { OptimizationTimeout = TimeSpan.FromSeconds(0.2) };
					return q;
				}
				var client = CreateClient("user", overrideDefaultQuotas: configureQuotas);

				// Create session
				SetupSession(_builder, client);

				// Hook up a task that completes when a session is deleted
				var deleted = new TaskCompletionSource<string>();
				((Server.Server)ServerFor(client)).SessionDeleted += (s, e) => { deleted.SetResult(e.Id); };

				// Run the action that may postpone the session timeout
				delayTimeout.Invoke(client);

				// Session should time out 200ms from now.
				// Verify to between 100 and 300ms.

				Thread.Sleep(100);

				// Session is not yet deleted
				Assert.False(deleted.Task.IsCompleted);
				Assert.Contains(client.GetServerStatus().Sessions, s => s.Id == SessionId);

				deleted.Task.Wait(millisecondsTimeout: 200);

				// Session is deleted
				Assert.True(deleted.Task.IsCompleted);
				Assert.Equal(SessionId, deleted.Task.Result);
				Assert.DoesNotContain(client.GetServerStatus().Sessions, s => s.Id == SessionId);
			});
		}
		}
	}
