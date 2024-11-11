using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Cim;
using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimNetworkConverter"/>.
	/// 
	/// A lot of its functionality is covered by <see cref="CimNetworkConverterDiginTests"/>
	/// and not repeated here
	/// </summary>
	[TestClass]
	public class CimNetworkConverterTests : CimBuilderTestFixture
	{
		private CimBuilder Builder => _networkBuilder;

		[TestMethod]
		public void ThreeCoilTransformersAreSupported()
		{
			// Specify a transformer
			var t = Builder.AddTransformer("transformer");

			Builder.AddEnd(t, "end1", 30);
			Builder.AddEnd(t, "end2", 20);
			Builder.AddEnd(t, "end3", 10);

			// Convert
			ConvertNetwork();

			// Verify that PGO created a tranformer with correct modes
			var transformer = _network.PowerTransformers.Single();

			var expected = new[] {
				"end1xx->end2xx 1.5, 1",
				"end2xx->end1xx 0.667, 1",
				"end1xx->end3xx 3, 1",
				"end3xx->end1xx 0.333, 1",
				"end2xx->end3xx 2, 1",
				"end3xx->end2xx 0.5, 1"
			};

			foreach (var m in transformer.Modes)
				Console.WriteLine(Describe(m));

			CollectionAssert.AreEquivalent(expected, transformer.Modes.Select(m => Describe(m)).ToList());


			string Describe(Transformer.Mode m) => FormattableString.Invariant($"{m.InputBus.Name}->{m.OutputBus.Name} {m.Ratio:G3}, {m.PowerFactor}");
		}

		[TestMethod]
		public void ConsumersAreCreatedFromEquivalentInjections()
		{
			Builder.AddEquivalentInjection("consumer");

			ConvertNetwork();

			var consumer = _network.Consumers.Single();
			Assert.AreEqual("consumer", consumer.Name);
		}

		[TestMethod]
		public void ProvidersAreCreatedFromEquivalentInjections()
		{
			// Negative minP/maxP indicate a provider
			var injection = Builder.AddEquivalentInjection("provider", 
				minPWatt: -200, maxPWatt: -100,
				minQVar: -100, maxQVar: 100);

			// We need a transformer to define the provider's generated voltage
			var trafo = Builder.AddTransformer("transformer");
			Builder.AddEnd(trafo, "end1", voltageVolts: 11_000, connectTo: injection.Terminals[0]);
			Builder.AddEnd(trafo, "end2", voltageVolts: 240);

			ConvertNetwork();


			Assert.IsFalse(_network.Consumers.Any());
			var producer = _network.Providers.Single();
			Assert.AreEqual(new Complex(200, 100), producer.GenerationCapacity);
			Assert.AreEqual(new Complex(100, -100), producer.GenerationLowerBound);
		}

		[TestMethod]
		public void ControllableAndBreakingSwitchTypesCanBeConfigured()
		{
			// Create a switch of each type
			Builder.AddSwitch<Fuse>("Fuse");
			Builder.AddSwitch<Jumper>("Jumper");
			Builder.AddSwitch<Sectionalizer>("Sectionalizer");
			Builder.AddSwitch<Disconnector>("Disconnector");
			Builder.AddSwitch<LoadBreakSwitch>("LoadBreakSwitch");
			Builder.AddSwitch<Breaker>("Breaker");
			Builder.AddSwitch<DisconnectingCircuitBreaker>("DisconnectingCircuitBreaker");
			Builder.AddSwitch<Recloser>("Recloser");
			var switches = Builder.CreatedObjects.OfType<Switch>();

			// Select 4 random types to be controllable and 4 random types to be breaking
			var r = new Random();
			List<string> controllableSwitches = switches.Shuffled(r).Take(4).Select(s => s.Name).ToList();
			List<string> breakingSwitches = switches.Shuffled(r).Take(4).Select(s => s.Name).ToList();

			// Configure and convert
			_networkOptions.ControllableSwitchTypes = controllableSwitches.Select(Enum.Parse<CimSwitchType>).ToList();
			_networkOptions.BreakingSwitchTypes = breakingSwitches.Select(Enum.Parse<CimSwitchType>).ToList();

			ConvertNetwork();

			// Verify
			CollectionAssert.AreEquivalent(controllableSwitches, _network.SwitchableLines.Select(s => s.Name).ToList());
			CollectionAssert.AreEquivalent(breakingSwitches, _network.Breakers.Select(s => s.Name).ToList());
		}
	}
}