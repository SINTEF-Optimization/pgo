using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;


namespace PgoRestDemo
{
	/// <summary>
	/// This program demonstrates how PGO can be used through the REST API.
	/// The program loads an example network and forecast, optimizes,
	/// shows the solution and removes the session and network.
	/// </summary>
	public class Program
	{
		public static void Main()
		{
			// Assumes that PGO is running at this address. This will be the case if you 
			// - start the project Sintef.Pgo.REST with the 'PgoREST' launch profile, or
			// - run 'docker run -p5000:80 sintef/pgo'
			string serviceUrl = @"http://localhost:5000";

			// Move to data directory
			while (!Directory.EnumerateFiles(".").Any(f => f.Contains("Sintef.Pgo.sln")))
				Directory.SetCurrentDirectory("..");
			Directory.SetCurrentDirectory("Data/Baran&Wu");

			new Program().RunAsync(serviceUrl).GetAwaiter().GetResult();
		}

		private string _networkFile = "baran-wu-modified_network.json";
		private string _forecastFile = "baran-wu-modified_forecast.json";
		private string _networkId = "myNetwork";
		private string _sessionId = "mySession";

		/// <summary>
		/// Test the PGO service at <paramref name="serviceUrl"/>. Assumes the current 
		/// directory contains the data files we need.
		/// </summary>
		/// <param name="serviceUrl">The root URL of the service</param>
		async Task RunAsync(string serviceUrl)
		{
			int waitTime = 2; // seconds
			double pingTime = 0.5; // seconds

			try
			{
				using HttpClient client = new HttpClient
				{
					BaseAddress = new Uri(serviceUrl)
				};

				Console.WriteLine($"*** Trying to contact server at {serviceUrl}...");
				var statusResponse = await client.GetAsync("api/server/");
				Show(statusResponse);

				Console.WriteLine();
				Console.WriteLine("*** Loading the power network...");
				var loadResponse = LoadNetwork(client);
				Show(loadResponse);

				Console.WriteLine();
				Console.WriteLine("*** Setting up a case...");
				var sessionResponse = CreateSession(client);
				Show(sessionResponse);

				// Run the optimization for a while
				Console.WriteLine();
				Console.WriteLine("*** Starting optimization...");
				var startResponse = await client.PutAsync($"api/sessions/{_sessionId}/runOptimization", new StringContent("true", Encoding.UTF8, "application/json"));
				Show(startResponse);

				Console.WriteLine();
				Console.WriteLine($"*** Waiting for {waitTime} seconds");
				for (int counter = 0; counter <= waitTime / pingTime; ++counter)
				{
					await Task.Delay((int)(pingTime * 1000));
					Console.Write(".");
				}
				Console.WriteLine(" finished!");

				Console.WriteLine();
				Console.WriteLine("*** Stopping optimization...");
				var stopResponse = await client.PutAsync($"api/sessions/{_sessionId}/stopOptimization", new StringContent("false", Encoding.UTF8, "application/json"));
				Show(stopResponse);

				Console.WriteLine();
				Console.WriteLine("*** Getting session status...");
				statusResponse = await client.GetAsync($"api/sessions/{_sessionId}");
				Show(statusResponse, showContents: true);

				Console.WriteLine();
				Console.WriteLine("*** Getting best solution...");
				var bestResponse = await client.GetAsync($"api/sessions/{_sessionId}/bestSolution");
				Show(bestResponse, showContents: true);

				Console.WriteLine();
				Console.WriteLine("*** Deleting session...");
				var deleteResponse = await client.DeleteAsync($"api/sessions/{_sessionId}");
				Show(deleteResponse);

				Console.WriteLine();
				Console.WriteLine("*** Deleting network...");
				deleteResponse = await client.DeleteAsync($"api/networks/{_networkId}");
				Show(deleteResponse);

				Console.WriteLine();
				Console.WriteLine("*** Finished");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void Show(HttpResponseMessage response, bool showContents = false)
		{
			if (response.IsSuccessStatusCode)
				Console.WriteLine($"Success, status {response.StatusCode}");
			else
			{
				Console.WriteLine($"Failure, status {response.StatusCode}");
				showContents = true;
			}

			if (showContents)
			{
				Console.WriteLine("Response contents:");
				Console.WriteLine(response.Content.ReadAsStringAsync().Result);
			}
		}

		private HttpResponseMessage LoadNetwork(HttpClient client)
		{
			using (var content = new MultipartFormDataContent())
			{
				content.Add(new StreamContent(File.OpenRead(_networkFile)), "networkDescription", _networkFile);

				return client.PostAsync($"api/networks/{_networkId}", content).Result;
			}
		}

		private HttpResponseMessage CreateSession(HttpClient client)
		{
			using (var content = new MultipartFormDataContent())
			{
				content.Add(new StringContent(_networkId), "networkId");
				content.Add(new StreamContent(File.OpenRead(_forecastFile)), "demands", _forecastFile);

				return client.PostAsync($"api/sessions/{_sessionId}", content).Result;
			}
		}
	}
}
