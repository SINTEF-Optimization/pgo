using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for NetworkBuilder
	/// </summary>
	[TestClass]
	public class NetworkBuilderTests
	{
		NetworkBuilder _builder;

		[TestInitialize]
		public void Setup()
		{
			_builder = new NetworkBuilder();
		}

		[TestMethod]
		public void DefaultBusIsConnection()
		{
			var bus = _builder.AddBus("Node1");

			Assert.AreEqual(BusTypes.Connection, bus.Type);
			Assert.AreEqual("Node1", bus.Name);
		}

		[TestMethod]
		public void CanAddAConsumer()
		{
			var bus = _builder.AddBus("Node1[consumption=(1, 0.2)]");

			Assert.AreEqual(BusTypes.PowerConsumer, bus.Type);
			Assert.AreEqual("Node1", bus.Name);
			Assert.AreEqual("<1; 0.2>", Format(_builder.Demand(bus)));
		}

		[TestMethod]
		public void CanAddAProducer()
		{
			var bus = _builder.AddBus("Node1[generatorVoltage=1000.0]");

			Assert.AreEqual(BusTypes.PowerProvider, bus.Type);
			Assert.AreEqual("Node1", bus.Name);
			Assert.AreEqual(1000, bus.GeneratorVoltage);
		}

		[TestMethod]
		public void CanAdd2WindingTransformer()
		{
			var bus1 = _builder.AddBus("Node1");
			var bus2 = _builder.AddBus("Node2");
			var transformerBus = _builder.AddBus("Transformer1[transformer;ends=(Node1,Node2);voltages=(1000,500);operation=fixed;factor=0.98]");

			Assert.AreEqual(BusTypes.PowerTransformer, transformerBus.Type);
			Assert.AreEqual("Transformer1", transformerBus.Name);
			Assert.IsTrue(transformerBus.IsTransformer);
			Assert.AreEqual(2, transformerBus.Transformer.Terminals.Count());
			Assert.AreEqual(2, transformerBus.Transformer.Modes.Count());

			Assert.AreEqual(1000.0, transformerBus.Transformer.ExpectedVoltageFor(bus1));
			Assert.AreEqual(500.0, transformerBus.Transformer.ExpectedVoltageFor(bus2));
		}



		[TestMethod]
		public void CanAdd2WindingTransformerInline()
		{
			_builder.Add("Node1 -- Line1 -- Transformer1[transformer;ends=(Node1,Node2);voltages=(1000,500);operation=fixed;factor=0.98] -- Line2 -- Node2");
			var bus1 = _builder.Network.GetBus("Node1");
			var bus2 = _builder.Network.GetBus("Node2");
			var transformerBus = _builder.Network.GetBus("Transformer1");


			Assert.AreEqual(BusTypes.PowerTransformer, transformerBus.Type);
			Assert.AreEqual("Transformer1", transformerBus.Name);
			Assert.IsTrue(transformerBus.IsTransformer);
			Assert.AreEqual(2, transformerBus.Transformer.Terminals.Count());
			Assert.AreEqual(2, transformerBus.Transformer.Modes.Count());

			Assert.AreEqual(1000.0, transformerBus.Transformer.ExpectedVoltageFor(bus1));
			Assert.AreEqual(500.0, transformerBus.Transformer.ExpectedVoltageFor(bus2));


			// Also with slightly different params
			_builder.Add("Node1 -- Conn1 -- Transformer2[voltages=(1000,100);operation=fixed;factor=0.98;transformer] -- Conn2 -- Node3");
			var bus3 = _builder.Network.GetBus("Node3");
			var transformerBus2 = _builder.Network.GetBus("Transformer2");


			Assert.AreEqual(BusTypes.PowerTransformer, transformerBus2.Type);
			Assert.AreEqual("Transformer2", transformerBus2.Name);
			Assert.IsTrue(transformerBus2.IsTransformer);
			Assert.AreEqual(2, transformerBus2.Transformer.Terminals.Count());
		}

		[TestMethod]
		public void CanAdd3WindingTransformer()
		{
			var bus1 = _builder.AddBus("Node1");
			var bus2 = _builder.AddBus("Node2");
			var bus3 = _builder.AddBus("Node3");
			var transformerBus = _builder.AddBus("Transformer1[transformer;ends=(Node1,Node2,Node3);voltages=(22000,11000,240);operation=auto;factor=0.98]");

			Assert.AreEqual(BusTypes.PowerTransformer, transformerBus.Type);
			Assert.AreEqual("Transformer1", transformerBus.Name);
			Assert.IsTrue(transformerBus.IsTransformer);
			Assert.AreEqual(3, transformerBus.Transformer.Terminals.Count());
			Assert.AreEqual(6, transformerBus.Transformer.Modes.Count());

			Assert.AreEqual(22000.0, transformerBus.Transformer.ExpectedVoltageFor(bus1));
			Assert.AreEqual(11000.0, transformerBus.Transformer.ExpectedVoltageFor(bus2));
			Assert.AreEqual(240.0, transformerBus.Transformer.ExpectedVoltageFor(bus3));
		}

		[TestMethod]
		public void CanAdd3WindingTransformerWithSpecifiedUpstreamBus()
		{
			var bus1 = _builder.AddBus("Node1");
			var bus2 = _builder.AddBus("Node2");
			var bus3 = _builder.AddBus("Node3");
			var transformerBus = _builder.AddBus("Transformer1[transformer;ends=(Node1,Node2,Node3);voltages=(22000,11000,240);operation=auto;factor=0.98;upstream=(Node1)]");

			Assert.AreEqual(BusTypes.PowerTransformer, transformerBus.Type);
			Assert.AreEqual(3, transformerBus.Transformer.Terminals.Count());
			
			// only modes from Node1 are generated
			Assert.AreEqual(2, transformerBus.Transformer.Modes.Count());
			Assert.IsTrue(transformerBus.Transformer.Modes.All(m => m.InputBus == bus1));
		}

		[TestMethod]
		public void CanAddALine()
		{
			var line = _builder.AddLine("Node1 -- Line[r=1.1] -- Node2");

			Assert.AreEqual("Line", line.Name);
			Assert.AreEqual("<1.1; 0>", Format(line.Impedance));

			Assert.AreEqual("Node1", line.Node1.Name);
			Assert.AreEqual("Node2", line.Node2.Name);
			Assert.AreEqual(line, _builder.Bus("Node1").IncidentLines.Single());
			Assert.AreEqual(line, _builder.Bus("Node2").IncidentLines.Single());
		}

		[TestMethod]
		public void CanAddTwoLines()
		{
			_builder.AddLine("Node1 -- Line1 -- Node2");
			_builder.AddLine("Node2 -- Line2 -- Node3");

			Assert.AreEqual(2, _builder.Bus("Node2").IncidentLines.Count());
		}

		[TestMethod]
		public void CanAddLinesAtOnce()
		{
			_builder.AddLines("Node1 -- Line1 -- Node2[generator] -- Line2 -- Node3");

			Assert.AreEqual(2, _builder.Bus("Node2").IncidentLines.Count());
		}

		[TestMethod]
		public void CanAddAnything()
		{
			_builder.Add("Node2");
			_builder.Add("Node2 -- Line1 -- NodeX");
			_builder.Add("Node1 -- Line2 -- Node2 -- Line3 -- Node3");

			Assert.AreEqual(3, _builder.Bus("Node2").IncidentLines.Count());
		}

		[TestMethod]
		public void CanSpecifyFaultProperties()
		{
			_builder.Add("N1 -- Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H] -- N2");

			var line = _builder.Network.Lines.Single();
			var faultProvider = _builder.LineFaultPropertiesProvider;

			Assert.AreEqual(0.1, faultProvider.FaultsPerYear(line));
			Assert.AreEqual(4, faultProvider.SectioningTime(line).TotalHours);
			Assert.AreEqual(2, faultProvider.RepairTime(line).TotalHours);
		}

		[TestMethod]
		public void CanSpecifyBreaker()
		{
			_builder.Add("N1 -- Line[breaker] -- N2 -- Line2 -- N3");

			Assert.IsTrue(_builder.Network.GetLine("Line").IsBreaker);
			Assert.IsFalse(_builder.Network.GetLine("Line2").IsBreaker);
		}

		[TestMethod]
		public void CanUseAnonymousNode()
		{
			_builder.Add("N1 -- B1[breaker] -o- S1a[closed] -o- Line1[z=(1,1)] -o- S1b[closed] -- N2");

			PowerNetwork network = _builder.Network;

			Assert.AreEqual(5, network.Buses.Count());
			Assert.AreEqual(4, network.LineCount);

			var line1 = network.GetLine("B1");
			var line2 = network.GetLine("S1a");
			var node = line1.Endpoints.Intersect(line2.Endpoints).Single();

			Assert.AreEqual("_anon_1", node.Name);
		}

		[TestMethod]
		public void CanSpecifyConsumerCategories()
		{
			_builder.Add("Consumer1[consumption=(1,0); type=Agriculture]");
			_builder.Add("Consumer2[consumption=(1,0); type=Domestic]");
			_builder.Add("Consumer3[consumption=(1,0); type=Industry]");
			_builder.Add("Consumer4[consumption=(1,0); type=Trade]");
			_builder.Add("Consumer5[consumption=(1,0); type=Public]");
			_builder.Add("Consumer6[consumption=(1,0); type= ElectricIndustry]");

			_builder.Add("Consumer7[consumption=(1,0); type=Agriculture/0.2,Industry/0.8]");

			TestUtils.AssertException(() => _builder.Add("Consumer8[consumption=(1,0); type=Agriculture/0.3,Industry/0.8]"),
				requiredMessage: "Consumption type 'Agriculture/0.3,Industry/0.8' does not add up to 1");

			AssertType(ConsumerCategory.Agriculture, "Consumer1");
			AssertType(ConsumerCategory.Domestic, "Consumer2");
			AssertType(ConsumerCategory.Industry, "Consumer3");
			AssertType(ConsumerCategory.Trade, "Consumer4");
			AssertType(ConsumerCategory.Public, "Consumer5");
			AssertType(ConsumerCategory.ElectricIndustry, "Consumer6");

			AssertType(ConsumerCategory.Agriculture, "Consumer7", 0.2);
			AssertType(ConsumerCategory.Industry, "Consumer7", 0.8);


			void AssertType(ConsumerCategory type, string consumer, double expectedFraction = 1.0)
			{
				var node = _builder.Network.GetBus(consumer);

				double fraction = _builder.ConsumerCategoryProvider.ConsumptionFraction(node, type);

				Assert.AreEqual(expectedFraction, fraction);
			}
		}

		[TestMethod]
		public void CanSpecifyMinMaxVoltages()
		{
			var bus1 = _builder.AddBus("Bus1");
			var bus2 = _builder.AddBus("Bus2[vMinV=500; vMaxV=1000]");

			// bus1 gets the defaults
			Assert.AreEqual(0, bus1.VMin);
			Assert.AreEqual(1e10, bus1.VMax);

			Assert.AreEqual(500, bus2.VMin);
			Assert.AreEqual(1000, bus2.VMax);
		}

		/// <summary>
		/// Formats a Complex to a simple, machine-independent string
		/// </summary>
		private static string Format(Complex value)
		{
			return value.ToString("g2", CultureInfo.InvariantCulture);
		}
	}
}
