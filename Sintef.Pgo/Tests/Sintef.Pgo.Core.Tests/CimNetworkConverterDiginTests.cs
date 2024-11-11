using System;
using System.IO;
using System.Linq;
using AngleSharp.Browser;
using C5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Core.IO;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimNetworkConverter"/>, based on the DIGIN dataset
	/// </summary>
	[TestClass]
	public class CimNetworkConverterDiginTests : CimTestFixture
	{
		[TestInitialize]
		public new void Setup()
		{
			base.Setup();

			ConvertNetwork(
				consumerMinVoltageFactor: 0.95,
				consumerMaxVoltageFactor: 1.05,
				lineImpedanceScaleFactor: 0.001);

			foreach (var warning in _networkConverter.Warnings)
				Console.WriteLine(warning);
		}

		[ClassCleanup]
		public static void ClassCleanup()
		{
			_diginNetworkParser = null;
		}

		[TestMethod]
		public void NetworkReport()
		{
			Console.WriteLine(_network.AnalyseNetwork(true));
		}

		[TestMethod]
		public void AggregatedNetworkReport()
		{
			var aggregation = NetworkAggregation.MakeAcyclicAndConnected(_network);

			Console.WriteLine(aggregation.AggregateNetwork.AnalyseNetwork(true));
		}

		[TestMethod]
		public void ConverterCreatesConnectionBuses()
		{
			Assert.AreEqual(101, _network.Connections.Count());

			var bus = _network.GetBus("517ac677-7ac0-dd4d-ac94-a191b7c05b13");
		}

		[TestMethod]
		public void ConverterCreatesLines()
		{
			// This number includes switches and auxiliary (e.g. transformer internal) lines
			Assert.AreEqual(119, _network.LineCount);


			// This line is 27km long, but still the Digin resistance value of 1950 Ohms seems excessive.
			// Using the option to scaling by 0.001 gives the more reasonable value 1.95 Ohms.

			// 132 FROLAND-T_ENGENE ACLS 1
			var line = _network.GetLine("92d33b99-7034-11eb-a65a-74e5f963e191");
			Assert.AreEqual(1.950, line.Resistance);
			Assert.AreEqual(10.530, line.Reactance);
			Assert.IsFalse(line.IsSwitchable);
			Assert.IsFalse(line.IsBreaker);
		}

		[TestMethod]
		public void ConverterCreatesProviders()
		{
			Assert.AreEqual(1, _network.Providers.Count());

			// Arendal 420kV Transmission Equivalent
			var provider = _network.GetBus("b7057d99-e21a-4ba1-9cef-15aeeee4232a");

			Assert.IsTrue(provider.IsProvider);
			Assert.AreEqual(420_000, provider.GeneratorVoltage);

			Assert.AreEqual(300_000_000, provider.ActiveGenerationCapacity);
			Assert.AreEqual(0, provider.ActiveGenerationLowerBound);

			Assert.AreEqual(300_000_000, provider.ReactiveGenerationCapacity);
			Assert.AreEqual(-100_000_000, provider.ReactiveGenerationLowerBound);
		}

		[TestMethod]
		public void ConverterCreatesTransformers()
		{
			Assert.AreEqual(4, _network.PowerTransformers.Count());

			// Arendal 420kV / 132kV Transformer 1
			var transformer = _network.PowerTransformers.Single(p => p.Name == "681a2fdd-5a55-11eb-a658-74e5f963e191");

			Assert.AreEqual(2, transformer.Modes.Count());
			var (mode1, mode2) = transformer.Modes.ToList();

			Assert.AreEqual("cb837454-5c66-d341-be63-d0c044e5fd3c", mode1.InputBus.Name);
			Assert.AreEqual("517ac677-7ac0-dd4d-ac94-a191b7c05b13", mode1.OutputBus.Name);
			Assert.AreEqual(TransformerOperationType.FixedRatio, mode1.Operation);
			Assert.AreEqual(420.0 / 132.0, mode1.Ratio);
			Assert.AreEqual(1, mode1.PowerFactor);

			Assert.AreEqual("517ac677-7ac0-dd4d-ac94-a191b7c05b13", mode2.InputBus.Name);
			Assert.AreEqual("cb837454-5c66-d341-be63-d0c044e5fd3c", mode2.OutputBus.Name);
			Assert.AreEqual(TransformerOperationType.FixedRatio, mode2.Operation);
			Assert.AreEqual(132.0 / 420.0, mode2.Ratio);
			Assert.AreEqual(1, mode2.PowerFactor);

		}

		[TestMethod]
		public void ConverterCreatesSwitchesAndBreakers()
		{
			Assert.AreEqual(31, _network.SwitchableLines.Count());
			Assert.AreEqual(29, _network.Breakers.Count());
			Assert.AreEqual(0, _network.SwitchableLines.Count(l => l.IsBreaker));

			// Telemarkstien2 400 Volt Breaker 12
			var normalSwitch = _network.SwitchableLines.Single(p => p.Name == "81f3131f-baed-442d-bf7a-3606832fc3e0");
			Assert.IsTrue(normalSwitch.IsSwitchable);
			Assert.IsFalse(normalSwitch.IsBreaker);

			// Telemarkstien2 400 Volt Fuse 1
			var breaker = _network.Breakers.Single(p => p.Name == "8aad5c29-6039-4321-8f8a-ad1c186dd145");
			Assert.IsTrue(normalSwitch.IsSwitchable);
			Assert.IsFalse(normalSwitch.IsBreaker);
		}

		[TestMethod]
		public void ConverterCreatesConsumers()
		{
			Assert.AreEqual(15, _network.Consumers.Count());

			// Telemarkstien2 400 Volt Conform Load 1
			var consumer = _network.Consumers.Single(p => p.Name == "eba80fde-c5f8-49fc-8465-0329fdeefda9");

			// Consumer voltage tolerance is (for now) set from converter options,
			// as it's not present in the Digin data
			Assert.AreEqual(380, consumer.VMin, 1e-6); // 95% of 400
			Assert.AreEqual(420, consumer.VMax, 1e-6); // 105% of 400
		}

		[TestMethod]
		public void ConverterConsidersOperationalLimits()
		{
			// 400V Telemarkstien 2 ACLineSegment 1
			var line = _network.GetLine("9d58e5bb-834c-4faa-928c-7da0bb1497d9");
			Assert.AreEqual(400, line.IMax, 1e-5);

			// 132 ARENDAL-FROLAND ACLS 1  (this one has multiple, equal limits)
			var line2 = _network.GetLine("92d33b99-7034-11eb-a65a-74e5f963e191");
			Assert.AreEqual(1020, line2.IMax, 1e-5);
		}

		[TestMethod]
		public void ConverterWarnsAboutUnsupportedFeatures()
		{
			Assert.AreEqual(2, _networkConverter.Warnings.Count());

			CollectionAssert.AreEquivalent(new[] {
				"No rated voltage found for RotatingMachine -- falling back to container's base voltage (3 occurrences, for e.g. 33666962-c2f9-4f6b-af5a-2ef1982ac282)",
				"Shunt compensators are not supported (1 occurrence, for 681a323d-5a55-11eb-a658-74e5f963e191)"
			}, _networkConverter.Warnings.ToList());
		}

		[TestMethod]
		public void DiginNetworkCanBeReadFromACombinedFile()
		{
			var parser = new CimJsonParser(new DiginUnitsProfile());
			parser.Parse(Path.Combine(TestUtils.DiginDir, "CombinedNetwork.jsonld"));
			parser.CreateCimObjects();

			ConvertNetwork(parser: parser);

			Assert.AreEqual(119, _network.LineCount);
			Assert.AreEqual(31, _network.SwitchableLines.Count());
			Assert.AreEqual(1, _network.Providers.Count());
			Assert.AreEqual(15, _network.Consumers.Count());
			Assert.AreEqual(4, _network.PowerTransformers.Count());
		}
	}
}