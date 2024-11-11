using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Cim;
using Sintef.Pgo.Core.IO;
using CurrentFlow = UnitsNet.ElectricCurrent;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimJsonParser"/>
	/// </summary>
	[TestClass]
	public class CimJsonParserTests
	{
		private static CimJsonParser _parser;
		private static CimJsonParser _sshParser;

		[TestInitialize]
		public void Setup()
		{
			_parser ??= TestUtils.ParseAllDiginData();
			_sshParser ??= TestUtils.ParseDiginSteadyStateData();
		}

		[ClassCleanup]
		public static void ClassCleanup()
		{
			_parser = null;
		}

		[TestMethod]
		public void ParserCanReadAllCimFilesInDiginFolder()
		{
			_parser.ReportTriplesAndObjects();

			Assert.AreEqual(19, _parser.GraphCount);
			Assert.AreEqual(10030, _parser.TripleCount);
			Assert.AreEqual(67, _parser.ObjectTypeCount);
		}

		[TestMethod]
		public void ParserCreatesCimObjects()
		{
			_parser.ReportCreatedObjects();
			_parser.ReportMissingObjects();

			var baseVoltages = _parser.CreatedObjects<BaseVoltage>().ToList();

			Assert.AreEqual(8, baseVoltages.Count);
		}

		[TestMethod]
		public void ParserCreatesNoDuplicateObjects()
		{
			// Some files contain uuids with an underscore. E.g. "urn:uuid:_681a26db-5a55-11eb-a658-74e5f963e191"
			// in DIGIN10-30-MV1_SC.jsonld.
			// This seems to indicate additional data for the ACLineSegment with ID "urn:uuid:681a26db-5a55-11eb-a658-74e5f963e191",
			// declared in DIGIN10-30-MV1_EQ.jsonld, without permission to create the object.
			// I cannot find documentation to confirm this, but for now, we make the parser avoid creating
			// objects for uuids with an underscore.

			var segment = _parser.ObjectWithUri("urn:uuid:681a26db-5a55-11eb-a658-74e5f963e191") as ACLineSegment;

			Assert.AreEqual("681a26db-5a55-11eb-a658-74e5f963e191", segment.MRID);

			Assert.IsNull(_parser.ObjectWithUri("urn:uuid:_681a26db-5a55-11eb-a658-74e5f963e191"));
		}

		[TestMethod]
		public void ParserFillsCimObjectProperties()
		{
			var baseVoltages = _parser.CreatedObjects<BaseVoltage>().ToList();

			// Verify that properties of the BaseVoltages have been set correctly

			Assert.AreEqual(876.32, baseVoltages.Sum(bv => bv.NominalVoltage.Value.Kilovolts));

			var base420 = baseVoltages.Single(x => x.NominalVoltage.Value.Kilovolts == 420);

			Assert.AreEqual("2dd90159-bdfb-11e5-94fa-c8f73332c8f4", base420.MRID);
			Assert.AreEqual("AC-420kV", base420.Name);
			Assert.AreEqual("Base Voltage 420 kV", base420.Description);
		}

		[TestMethod]
		public void ParserFillsCimObjectReferences()
		{
			_parser.ReportMissingObjects();

			var segments = _parser.CreatedObjects<ACLineSegment>().ToList();

			var segment = segments.Single(x => x.Name == "04 TELEMA2 ACLS2");

			// Verify that a reference to the correct BaseVoltage has been created

			var bv = segment.BaseVoltage;
			Assert.AreEqual("9598e4a0-67e5-4ad7-879c-c85a1f63159c", bv.MRID);
			Assert.AreEqual(400, bv.NominalVoltage.Value.Volts);
		}

		[TestMethod]
		public void ParserFillsReverseRelationLists()
		{
			var transformer = _parser.CreatedObjects<PowerTransformer>().First();

			// Verify that PowerTransformer.PowerTransformerEnds has been been filled
			// with each PowerTransformerEnd that references it in the PowerTransformer property

			var ends = transformer.PowerTransformerEnds.ToList();
			var expectedEnds = _parser.CreatedObjects<PowerTransformerEnd>()
				.Where(end => end.PowerTransformer == transformer)
				.ToList();

			CollectionAssert.AreEquivalent(expectedEnds, ends);
		}

		[TestMethod]
		public void ParserFillsEnums()
		{
			var machine = _parser.CreatedObject<SynchronousMachine>("33666962-c2f9-4f6b-af5a-2ef1982ac282");

			Assert.AreEqual(SynchronousMachineKind.Generator, machine.Type);
		}
	}
}