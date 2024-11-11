using Newtonsoft.Json;
using Sintef.Pgo.Api.Factory;
using Sintef.Pgo.DataContracts;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace PgoRestDemo
{
	/// <summary>
	/// This program demonstrates how PGO can be used through the .NET API.
	/// The program loads an example network and forecast, optimizes,
	/// shows the solution and removes the session and network.
	/// </summary>
	public class Program
	{
		public static void Main()
		{
			// Instantiate the server
			var server = ServerFactory.CreateServer();

			// Move to data directory
			while (!Directory.EnumerateFiles(".").Any(f => f.Contains("Sintef.Pgo.sln")))
				Directory.SetCurrentDirectory("..");
			Directory.SetCurrentDirectory("Data/Baran&Wu");

			new Program().Run(server);
		}

		private string _networkFile = "baran-wu-modified_network.json";
		private string _forecastFile = "baran-wu-modified_forecast.json";
		private string _networkId = "myNetwork";
		private string _sessionId = "mySession";

		/// <summary>
		/// Test the PGO service at <paramref name="serviceUrl"/>. Assumes the current 
		/// directory contains the data files we need.
		/// </summary>
		void Run(Sintef.Pgo.Api.IServer server)
		{
			int waitTime = 2; // seconds
			double pingTime = 0.5; // seconds

			try
			{
				Show(server.Status);

				Console.WriteLine();
				Console.WriteLine("*** Loading the power network...");
				PowerGrid network = Deserialize<PowerGrid>(_networkFile);
				server.AddNetwork(_networkId, network);

				Console.WriteLine();
				Console.WriteLine("*** Setting up a case...");
				var parameters = new SessionParameters
				{
					NetworkId = _networkId,
					Demand = Deserialize<Demand>(_forecastFile)
				};
				var session = server.AddSession(_sessionId, parameters);

				// Run the optimization for a while
				Console.WriteLine();
				Console.WriteLine("*** Starting optimization...");
				session.StartOptimization();

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
				session.StopOptimization();

				Console.WriteLine();
				Console.WriteLine("*** Getting session status...");
				Show(session.Status);

				Console.WriteLine();
				Console.WriteLine("*** Getting best solution...");
				var bestSolution = session.BestJsonSolution;
				Show(bestSolution);

				Console.WriteLine();
				Console.WriteLine("*** Deleting session...");
				server.RemoveSession(session);

				Console.WriteLine();
				Console.WriteLine("*** Deleting network...");
				server.RemoveNetwork(_networkId);
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
			}
		}

		private T Deserialize<T>(string fileName)
		{
			using StreamReader reader = new StreamReader(fileName) ;
			using JsonTextReader jsonReader = new JsonTextReader(reader);

			return new JsonSerializer().Deserialize<T>(jsonReader);
		}

		private void Show(object data)
		{
			new JsonSerializer().Serialize(Console.Out, data);
			Console.WriteLine();
		}
	}
}
