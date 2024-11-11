using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Client;
using Xunit;

namespace Sintef.Pgo.REST.Tests
{
	public class TestServerFixture : IClassFixture<WebApplicationFactory<Startup>>
	{
		private readonly WebApplicationFactory<Startup> _factory;

		public TestServerFixture(WebApplicationFactory<Startup> factory)
		{
			_factory = factory;
		}

		internal (HttpClient client, TestServer server) SetupServer()
		{
			var server = new TestServer();
			var multiServer = new TestMultiServer(server);

			var client = _factory
				.WithWebHostBuilder(builder =>
				{
					builder
						.ConfigureTestServices(services =>
						{
							services.AddSingleton<IMultiUserServer>(multiServer);
						});
				}).CreateClient();

			return (client, server);
		}
	}
}