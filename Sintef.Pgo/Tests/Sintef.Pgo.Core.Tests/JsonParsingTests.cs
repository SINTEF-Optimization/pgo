using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using System.Collections.Generic;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class JsonParsingTest
	{
		[TestMethod]
		public void CanParseBaranWu()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-modified.json"));
			Assert.AreEqual("Baran-wu case", network.Name);
			int countnodes = network.Buses.Count();
			Assert.AreEqual(34, countnodes);
			int countlines = network.LineCount;
			Assert.AreEqual(38, countlines);

		}

		[TestMethod]
		public void ReadWriteBaranWuIsEqual()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-modified.json"));
			using (MemoryStream ms = new MemoryStream())
			{
				IO.PgoJsonParser.WriteNetwork(network, ms, true);

				// Now go back to the beginning and reparse the network
				ms.Seek(0, SeekOrigin.Begin);
				var reparsedNetwork = IO.PgoJsonParser.ParseNetworkFromJson(ms);

				VerifyEqualNetworks(network, reparsedNetwork);
			}
		}

		/// <summary>
		/// Tests that the reading cloning, and then writing of transformers returns the same problem definiiton.
		/// </summary>
		[TestMethod]
		public void ReadWriteBaranWuIsEqualForTransformers()
		{
			//Read
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-mod_w_trans.json"));

			//Clone
			network = network.Clone();

			using (MemoryStream ms = new MemoryStream())
			{
				IO.PgoJsonParser.WriteNetwork(network, ms, true);

				// Now go back to the beginning and reparse the network
				ms.Seek(0, SeekOrigin.Begin);
				var reparsedNetwork = IO.PgoJsonParser.ParseNetworkFromJson(ms);

				VerifyEqualNetworks(network, reparsedNetwork);
			}
		}

		[TestMethod]
		public void ReadWriteBaranWuLoadIsEqual()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-modified.json"));
			var forecasts = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu-modified_forecast.json"));
			IFlowProvider flowProvider = Utils.CreateFlowProvider(FlowApproximation.IteratedDF);
			var problem = new PgoProblem(forecasts, flowProvider, "DummyName");

			using (MemoryStream ms = new MemoryStream())
			{
				IO.PgoJsonParser.WriteDemands(problem.AllPeriodData, ms, true);

				// Now go back to the beginning and reparse the network
				ms.Seek(0, SeekOrigin.Begin);
				var reparsedDemands = IO.PgoJsonParser.ParseDemandsFromJson(network, ms, true);

				Assert.AreEqual(forecasts.Count, reparsedDemands.Count);
				for (int i = 0; i < forecasts.Count; ++i)
				{
					var forecast = forecasts[i];
					var otherForecast = reparsedDemands[i];

					Assert.AreEqual(forecast.Period.StartTime, otherForecast.Period.StartTime);
					Assert.AreEqual(forecast.Period.EndTime, otherForecast.Period.EndTime);

					foreach (var bus in network.Consumers)
					{
						Assert.AreEqual(forecast.Demands.ActivePowerDemand(bus), otherForecast.Demands.ActivePowerDemand(bus));
						Assert.AreEqual(forecast.Demands.ReactivePowerDemand(bus), otherForecast.Demands.ReactivePowerDemand(bus));
					}
				}
			}
		}

		[TestMethod]
		public void ReadWriteBaranWuMultiperiodLoadIsEqual()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-modified.json"));
			var forecasts = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu-modified_forecast-multiperiod.json"));
			IFlowProvider flowProvider = Utils.CreateFlowProvider(FlowApproximation.IteratedDF);
			var problem = new PgoProblem(forecasts, flowProvider, "DummyName");

			using (MemoryStream ms = new MemoryStream())
			{
				IO.PgoJsonParser.WriteDemands(problem.AllPeriodData, ms, true);

				// Now go back to the beginning and reparse the network
				ms.Seek(0, SeekOrigin.Begin);
				var reparsedForecasts = IO.PgoJsonParser.ParseDemandsFromJson(network, ms, true);

				Assert.AreEqual(forecasts.Count, reparsedForecasts.Count);
				for (int i = 0; i < forecasts.Count; ++i)
				{
					var forecast = forecasts[i];
					var otherForecast = reparsedForecasts[i];

					Assert.AreEqual(forecast.Period.StartTime, otherForecast.Period.StartTime);
					Assert.AreEqual(forecast.Period.EndTime, otherForecast.Period.EndTime);

					foreach (var bus in network.Consumers)
					{
						Assert.AreEqual(forecast.Demands.ActivePowerDemand(bus), otherForecast.Demands.ActivePowerDemand(bus));
						Assert.AreEqual(forecast.Demands.ReactivePowerDemand(bus), otherForecast.Demands.ReactivePowerDemand(bus));
					}
				}
			}
		}

		[TestMethod]
		public void CanParseBaranWuForecast()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu-modified.json"));
			PeriodData parsedPeriodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu-modified_forecast.json"))[0];
			var parsedDemands = parsedPeriodData.Demands;

			//Three spot checks
			Assert.AreEqual(1e6 * 0.06, parsedDemands.ActivePowerDemand(network.GetBus("5")));
			Assert.AreEqual(1e6 * 0.03, parsedDemands.ReactivePowerDemand(network.GetBus("5")));
			Assert.AreEqual(1e6 * 0.2, parsedDemands.ActivePowerDemand(network.GetBus("7")));
			Assert.AreEqual(1e6 * 0.1, parsedDemands.ReactivePowerDemand(network.GetBus("7")));
			Assert.AreEqual(1e6 * 0.06, parsedDemands.ActivePowerDemand(network.GetBus("12")));
			Assert.AreEqual(1e6 * 0.035, parsedDemands.ReactivePowerDemand(network.GetBus("12")));
		}

		[TestMethod]
		public void CanParseConsumerTypes()
		{
			var parsedProviderExample = PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-consumertypes_providerbus.json"));

			Assert.AreEqual(1, parsedProviderExample.Buses.Count());


			var parsedConsumerNoType = PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-consumertypes_consumerbus_notype.json"));
			Assert.IsFalse(parsedConsumerNoType.CategoryProvider.HasDataFor(parsedConsumerNoType.GetBus("1")));

			var parsedConsumerType = PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-consumertypes_consumerbus_correcttype.json"));
			Assert.IsTrue(parsedConsumerType.CategoryProvider.ConsumptionFraction(parsedConsumerType.GetBus("1"), ConsumerCategory.Industry) == 1.0);

			try //verify that this throws
			{
				var parsedWrongConsumerType = PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-consumertypes_consumerbus_incorrecttype.json"));
				Assert.Fail();
			}
			catch (JsonSerializationException)
			{ }

		}

		[TestMethod]
		public void CanParseLineFaultProperties()
		{
			var parsedPowerNetwork = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-linefaultproperties_network.json"));
			Line line = parsedPowerNetwork.GetLine("1", "2");
			parsedPowerNetwork.PropertiesProvider.FaultsPerYear(line);
			Assert.IsTrue(parsedPowerNetwork.PropertiesProvider.FaultsPerYear(line).Equals(12));
			Assert.IsTrue(parsedPowerNetwork.PropertiesProvider.SectioningTime(line).Minutes.Equals(5));
			Assert.IsTrue(parsedPowerNetwork.PropertiesProvider.RepairTime(line).Hours.Equals(6));

		}

		[TestMethod]
		public void CanParseSinglePeriodSwitchSettings()
		{
			var parsedPowerNetwork = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-singleperiodswitchsettings_network.json"));
			var parsedSwitchSettingsCorrect = IO.PgoJsonParser.ParseSingleConfigurationFromSolution(parsedPowerNetwork, File.ReadAllText(TestUtils.TestDataFile("parsetest-singleperiodswitchsettings_settingscorrect.json")));

			// Assert that the line switch1 is open.
			Assert.IsTrue(parsedSwitchSettingsCorrect.IsOpen(parsedPowerNetwork.GetLine("switch1")));

			// Assert that it throws when unswitchable line is parsed.
			try
			{
				var parsedSwitchSettingsUnswitchableLine = IO.PgoJsonParser.ParseSingleConfigurationFromSolution(parsedPowerNetwork, File.ReadAllText(TestUtils.TestDataFile("parsetest-singleperiodswitchsettings_settingsincorrect.json")));
				Assert.Fail();
			}
			catch (Exception)
			{ }

		}

		[TestMethod]
		public void CanParseStartConfigurationFromJSON()
		{
			var parsedPowerNetwork = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("parsetest-singleperiodswitchsettings_network.json"));
			NetworkConfiguration parsedSwitchSettingsCorrect = IO.PgoJsonParser.ParseSingleConfigurationFromSolution(parsedPowerNetwork, File.ReadAllText(TestUtils.TestDataFile("parsetest-singleperiodswitchsettings_settingscorrect.json")));

			//Get a config string in the right format
			NetworkConfiguration startConfig = ConvertToParsedStartConfiguration(parsedSwitchSettingsCorrect);

			// Assert that the line switch1 is open.
			Assert.IsTrue(startConfig.IsOpen(parsedPowerNetwork.GetLine("switch1")));

			//Takes a network configuration as input, and converts it, by various gymnastics,
			//to a start configuration that has been parsed by PgoJsonParser.ParseStartConfigurationFromJSON
			NetworkConfiguration ConvertToParsedStartConfiguration(NetworkConfiguration singlePeriodConfig)
			{
				Period per = new Period(new DateTime(2000, 1, 1), new DateTime(2000, 1, 2), 0, "0");
				SinglePeriodSettings singlePeriodSettingsContract = PgoJsonParser.CreateSinglePeriodSettingsContract(per, singlePeriodConfig.SwitchSettings);
				string singlePeriodOnString = PgoJsonParser.Serialize(singlePeriodSettingsContract, true);

				//Now parse this string 
				Stream stream = singlePeriodOnString.ToMemoryStream();
				return PgoJsonParser.ParseConfigurationFromStream(parsedPowerNetwork, stream);
			}
		}


		[TestMethod]
		public void TestMultiPeriodSolutionJSON_IO()
		{
			CriteriaTests criteriaTests = new CriteriaTests();
			criteriaTests.SetUp();
			PgoSolution sol = criteriaTests.CreateInitialMultiPeriodSolution(FlowApproximation.SimplifiedDF, false);

			//Write, read, and compare
			using (MemoryStream ms = new MemoryStream())
			{
				PgoJsonParser.WriteJSONConfigurations(sol, ms, prettify: true);

				// Now go back to the beginning and reparse the configurations
				ms.Seek(0, SeekOrigin.Begin);
				Dictionary<Period, NetworkConfiguration> configs = PgoJsonParser.ParseMultiPeriodJSONConfiguration(sol.Problem, ms);

				foreach ((Period period, SwitchSettings solConfig) tup in sol.SwitchSettingsPerPeriod)
				{
					Assert.IsTrue(configs.ContainsKey(tup.period), "Original configuration not retrieved after writing and parsing.");
					Assert.IsTrue(tup.solConfig.Equals(configs[tup.period].SwitchSettings));
				}
			}
		}

		/// <summary>
		/// Verifies that <paramref name="network"/> and <paramref name="otherNetwork"/> are equal
		/// </summary>
		/// <param name="network"></param>
		/// <param name="otherNetwork"></param>
		private static void VerifyEqualNetworks(PowerNetwork network, PowerNetwork otherNetwork)
		{
			Assert.AreEqual(network.Name, otherNetwork.Name);
			Assert.AreEqual(network.Buses.Count(), otherNetwork.Buses.Count());
			Assert.AreEqual(network.LineCount, otherNetwork.LineCount);

			// Check transformers
			foreach (var transformer in network.PowerTransformers)
			{
				Transformer other = otherNetwork.PowerTransformers.SingleOrDefault(t => t.Bus.Name == transformer.Bus.Name);
				Assert.AreNotEqual(null, other);

				Assert.AreEqual(transformer.Terminals.Count(), other.Terminals.Count());
				foreach (var terminal in transformer.Terminals)
				{
					Bus otherTerminal = other.Terminals.SingleOrDefault(t => t.Name == terminal.Name);
					Assert.AreNotEqual(null, otherTerminal);
					Assert.AreEqual(transformer.ExpectedVoltageFor(terminal), other.ExpectedVoltageFor(otherTerminal));
				}

				Assert.AreEqual(transformer.Modes.Count(), other.Modes.Count());
				foreach (Transformer.Mode mode in transformer.Modes)
				{
					Transformer.Mode otherMode = other.Modes.SingleOrDefault(m => m.Equals(mode));
					Assert.AreNotEqual(null, otherMode);
				}
			}

			// Check buses
			foreach (var bus in network.Buses)
			{
				var otherBus = otherNetwork.GetBus(bus.Name);
				Assert.AreEqual(bus.Name, otherBus.Name);
				Assert.AreEqual(bus.Type, otherBus.Type);

				// Check locations
				if (bus.Location != null)
				{
					Assert.IsTrue(otherBus.Location != null);
					Assert.AreEqual(bus.Location.X, otherBus.Location.X);
					Assert.AreEqual(bus.Location.Y, otherBus.Location.Y);
				}

				if (!bus.IsProvider)
				{
					Assert.AreEqual(bus.VMin, otherBus.VMin);
					Assert.AreEqual(bus.VMax, otherBus.VMax);
				}
				if (bus.IsConsumer)
				{
					Assert.AreEqual(network.CategoryProvider.Categories(bus).Single(), otherNetwork.CategoryProvider.Categories(otherBus).Single());
				}
				if (bus.IsProvider)
				{
					Assert.AreEqual(bus.ActiveGenerationCapacity, otherBus.ActiveGenerationCapacity);
					Assert.AreEqual(bus.ReactiveGenerationCapacity, otherBus.ReactiveGenerationCapacity);
					Assert.AreEqual(bus.ActiveGenerationLowerBound, otherBus.ActiveGenerationLowerBound);
					Assert.AreEqual(bus.ReactiveGenerationLowerBound, otherBus.ReactiveGenerationLowerBound);
					Assert.AreEqual(bus.GeneratorVoltage, otherBus.GeneratorVoltage);
				}

			}

			// Check lines
			foreach (var line in network.Lines)
			{
				var otherLine = otherNetwork.GetLine(line.Node1, line.Node2);
				Assert.AreEqual(line.Name, otherLine.Name);
				Assert.AreEqual(line.Node1.Name, otherLine.Node1.Name);
				Assert.AreEqual(line.Node2.Name, otherLine.Node2.Name);
				Assert.AreEqual(line.IMax, otherLine.IMax);
				Assert.AreEqual(line.VMax, otherLine.VMax);
				Assert.AreEqual(line.Impedance, otherLine.Impedance);
				Assert.AreEqual(line.IsSwitchable, otherLine.IsSwitchable);
				if (line.IsSwitchable)
					Assert.AreEqual(line.SwitchingCost, otherLine.SwitchingCost);
				Assert.AreEqual(line.IsBreaker, otherLine.IsBreaker);
				Assert.AreEqual(network.PropertiesProvider.FaultsPerYear(line), otherNetwork.PropertiesProvider.FaultsPerYear(otherLine));
				Assert.AreEqual(network.PropertiesProvider.SectioningTime(line), otherNetwork.PropertiesProvider.SectioningTime(otherLine));
				Assert.AreEqual(network.PropertiesProvider.RepairTime(line), otherNetwork.PropertiesProvider.RepairTime(otherLine));
			}
		}
	}
}