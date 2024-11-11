using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for the REST interface
	/// </summary>
	/// <remarks>See <a href="https://docs.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-2.2"/>
	/// for more information about integration tests with Asp.NET Core.</remarks>
	public class RESTInterfaceTests : TestServerFixture
	{
		public RESTInterfaceTests(WebApplicationFactory<Startup> factory) : base(factory)
		{
		}

		[Fact]
		public async Task Post_LoadNetworkSuccess()
		{
			// Arrange
			var (client, server) = SetupServer();

			var filecontent = "Network layout".ToMemoryStream();
			var filecontent2 = "limits".ToMemoryStream();
			HttpResponseMessage response;
			var content = new MultipartFormDataContent();
			content.Add(new StreamContent(filecontent), "networkDescription", "PowerDemandsFilename");
			content.Add(new StreamContent(filecontent2), "limits", "PowerDemandsFilename");

			var networkId = "myNetwork";
			// Act
			response = await client.PostAsync($"/api/networks/{networkId}", content);

			// Assert
			response.EnsureSuccessStatusCode(); // Status Code 200-299
			Assert.Equal("Network layout", server.Network);

			// Clean up
			filecontent.Dispose();
			content.Dispose();
		}

		[Fact]
		[Trait("Category", "REST")]
		public async Task Post_CreateJsonSessionWithoutStartConfiguration()
		{
			// Arrange
			var (client, server) = SetupServer();
			server.NetworkName = "network";
			server.NetworkIds = new[] { "myNetwork" };

			var content = new MultipartFormDataContent();

			Stream problem = "JSON File".ToMemoryStream();

			content.Add(new StringContent("myNetwork"), "networkId");
			content.Add(new StreamContent(problem), "demands", "Name of forecast file");
			content.Add(new StreamContent(Stream.Null), "startConfiguration");


			// Act
			var response = await client.PostAsync(
				"/api/sessions/4",
				content);

			// Assert
			response.EnsureSuccessStatusCode(); // Status Code 200-299
			Assert.Equal("JSON File", server.LastCreatedJsonSession.ProblemContent);
			Assert.Equal("4", server.LastCreatedJsonSession.Id);

			// Clean up
			content.Dispose();
			problem.Dispose();
		}

		[Fact]
		[Trait("Category", "REST")]
		public async Task Post_CreateJsonSessionWithStartConfiguration()
		{
			// Arrange
			var (client, server) = SetupServer();
			server.NetworkName = "network";
			server.NetworkIds = new[] { "myNetwork" };


			var content = new MultipartFormDataContent();

			Stream problem = "JSON File".ToMemoryStream();
			Stream currentConfig = "Current configuration".ToMemoryStream();

			content.Add(new StringContent("myNetwork"), "networkId");
			content.Add(new StreamContent(problem), "demands", "Name of forecast file");
			content.Add(new StreamContent(currentConfig), "startConfiguration", "Name of configuration file");


			// Act
			var response = await client.PostAsync(
				"/api/sessions/5",
				content);

			// Assert
			response.EnsureSuccessStatusCode(); // Status Code 200-299
			Assert.Equal("JSON File", server.LastCreatedJsonSession.ProblemContent);
			Assert.Equal("Current configuration", server.LastCreatedJsonSession.ConfigurationContent);
			Assert.Equal("5", server.LastCreatedJsonSession.Id);

			// Clean up
			content.Dispose();
			problem.Dispose();
		}

	}
}
