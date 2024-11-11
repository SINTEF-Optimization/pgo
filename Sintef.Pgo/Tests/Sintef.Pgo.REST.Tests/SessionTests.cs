using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Newtonsoft.Json;
using Xunit;

using Moq;
using Sintef.Pgo.REST.Client;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST.Tests
{
	public class SessionTests : IClassFixture<WebApplicationFactory<Startup>>
	{
		private readonly WebApplicationFactory<Startup> _factory;
		private HttpClient _client;
		Mock<IServer> _service;

		public SessionTests(WebApplicationFactory<Startup> factory)
		{
			_factory = factory;
		}

		/// <summary>
		/// Sets up the client with a mock service that contains a single mock 
		/// session with the given ID
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <returns>The mock session</returns>
		private Mock<ISession> SetupSingleSession(string id, double? bestSolutionValue = null, bool? isOptimizing = null)
		{
			var session = new Mock<ISession>();
			session.Setup(s => s.Id).Returns(id);
			if (bestSolutionValue.HasValue)
			{
				session.Setup(s => s.BestSolution).Returns(new Mock<IPgoSolution>().Object);
				session.Setup(s => s.BestSolutionIsFeasible).Returns(true);
				session.Setup(s => s.BestSolutionValue).Returns(bestSolutionValue.Value);
			}
			if (isOptimizing.HasValue)
				session.Setup(s => s.OptimizationIsRunning).Returns(isOptimizing.Value);

			_service = new Mock<IServer>();

			_service.Setup(s => s.GetSession(It.IsAny<string>())).Returns((ISession)null);
			_service.Setup(s => s.GetSession(id)).Returns(session.Object);

			var multiServer = new TestMultiServer(_service.Object);

			_client = _factory
				.WithWebHostBuilder(builder =>
				{
					builder.ConfigureTestServices(services =>
					{
						services.AddSingleton<IMultiUserServer>(multiServer);
					});
				}).CreateClient();

			return session;
		}

		[Fact]
		public async Task Get_Status()
		{
			// Arrange
			var session = SetupSingleSession("3", bestSolutionValue: 0.4, isOptimizing: true);

			// Act
			var response = await _client.GetAsync("/api/sessions/3");

			// Assert
			response.EnsureSuccessStatusCode(); // Status Code 200-299
			var content = await response.Content.ReadAsStringAsync();
			var status = JsonConvert.DeserializeObject<SessionStatus>(content);

			Assert.Equal("3", status.Id);
			Assert.Equal(0.4, status.BestSolutionValue);
			Assert.True(status.OptimizationIsRunning);
			
			response.Dispose();
		}

		[Theory]
		[InlineData(HttpStatusCode.OK, true, "ExistingSession", true)]
		[InlineData(HttpStatusCode.OK, true, "ExistingSession", false)]
		[InlineData(HttpStatusCode.OK, false, "ExistingSession", true)]
		[InlineData(HttpStatusCode.OK, false, "ExistingSession", false)]
		[InlineData(HttpStatusCode.NotFound, true, "NonExistingSession", true)]
		[InlineData(HttpStatusCode.NotFound, true, "NonExistingSession", false)]
		[InlineData(HttpStatusCode.NotFound, false, "NonExistingSession", true)]
		[InlineData(HttpStatusCode.NotFound, false, "NonExistingSession", false)]
		public async Task Put_RunOptimization(
			HttpStatusCode expectedStatusCode,
			bool runFlag,
			string sessionId,
			bool optimizationIsRunning)
		{
			// Arrange
			var session = SetupSingleSession("ExistingSession");

			session.Setup(s => s.OptimizationIsRunning).Returns(optimizationIsRunning);

			// Act
			var httpContent = new StringContent(JsonConvert.SerializeObject(runFlag), Encoding.UTF8, "application/json");
			var response = await _client.PutAsync(
				$"/api/sessions/{sessionId}/runOptimization", httpContent);

			// Assert
			Assert.Equal(expectedStatusCode, response.StatusCode);
			if (response.StatusCode == HttpStatusCode.OK)
			{
				session.Verify(s => s.OptimizationIsRunning);

				if (runFlag && !optimizationIsRunning)
					session.Verify(s => s.StartOptimization(It.IsAny<Scoop.Kernel.StopCriterion>()));
				if (!runFlag && optimizationIsRunning)
					session.Verify(s => s.StopOptimization());

				session.VerifyNoOtherCalls();
			}

			// Clean up
			response.Dispose();
		}

		[Theory]
		[InlineData("ExistingSession")]
		[InlineData("NonExistingSession")]
		public async Task Get_BestSolution(string sessionId)
		{
			// Arrange
			var session = SetupSingleSession("ExistingSession");

			var solution = new Mock<IPgoSolution>();
			solution.Setup(s => s.ToJson(It.IsAny<IFlowProvider>(), true)).Returns("{}");
			session.Setup(s => s.BestSolution).Returns(solution.Object);
			session.Setup(s => s.GetBestSolutionClone()).Returns(solution.Object);

			// Act
			var response = await _client.GetAsync(
				$"/api/sessions/{sessionId}/bestSolution");

			// Assert
			Assert.Equal(sessionId == "ExistingSession" ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
			if (response.StatusCode == HttpStatusCode.OK)
				Assert.Equal("{}", await response.Content.ReadAsStringAsync());

			// Clean up
			response.Dispose();
		}

		[Theory]
		[InlineData("ExistingSession")]
		[InlineData("NonExistingSession")]
		public async Task Delete_Session(string sessionId)
		{
			// Arrange
			var session = SetupSingleSession("ExistingSession");

			// Act
			var response = await _client.DeleteAsync($"/api/sessions/{sessionId}");

			// Assert
			if (sessionId == "ExistingSession")
			{
				_service.Verify(s => s.DeleteSession(sessionId));
				Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			}
			else
				Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

			// Clean up
			response.Dispose();
		}
	}

}
