using Sintef.Pgo.DataContracts;
using Sintef.Pgo.REST.Client;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace PgoRestDemo
{
	/// <summary>
	/// This program demonstrates how PGO can be used through <see cref="PgoRestClient"/>.
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

			new Program().Run(serviceUrl);
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
		void Run(string serviceUrl)
		{
			int waitTime = 2; // seconds
			double pingTime = 0.5; // seconds

			try
			{
				PgoRestClient client = new PgoRestClient(SessionType.Json, serviceUrl, null);

				Console.WriteLine($"*** Trying to contact server at {serviceUrl}...");
				client.GetServerStatus();

				Console.WriteLine();
				Console.WriteLine("*** Loading the power network...");
				client.LoadNetwork(_networkId, File.OpenRead(_networkFile), _networkFile);

				Console.WriteLine();
				Console.WriteLine("*** Setting up a case...");
				client.CreateJsonSession(_sessionId, _networkId, _forecastFile);

				// Run the optimization for a while
				Console.WriteLine();
				Console.WriteLine("*** Starting optimization...");
				client.StartOptimizing(_sessionId);

				Console.WriteLine();
				Console.WriteLine($"*** Waiting for {waitTime} seconds");
				for (int counter = 0; counter <= waitTime / pingTime; ++counter)
				{
					Thread.Sleep((int)(pingTime * 1000));
					Console.Write(".");
				}
				Console.WriteLine(" finished!");

				Console.WriteLine();
				Console.WriteLine("*** Stopping optimization...");
				client.StopOptimizing(_sessionId);

				Console.WriteLine();
				Console.WriteLine("*** Getting session status...");
				var status = client.GetSessionStatus(_sessionId);
				Show(status);

				Console.WriteLine();
				Console.WriteLine("*** Getting best solution...");
				var bestSolution = client.GetBestSolution(_sessionId);
				Show(bestSolution);

				Console.WriteLine();
				Console.WriteLine("*** Deleting session...");
				client.DeleteSession(_sessionId);

				Console.WriteLine();
				Console.WriteLine("*** Deleting network...");
				client.DeleteNetwork(_networkId);

				Console.WriteLine();
				Console.WriteLine("*** Finished");
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private void Show(object data)
		{
			Console.WriteLine(JsonSerializer.Serialize(data, data.GetType()));
		}
	}
}
