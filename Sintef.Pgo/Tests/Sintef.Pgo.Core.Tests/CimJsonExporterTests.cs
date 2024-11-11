using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimJsonExporter"/>
	/// </summary>
	[TestClass]
	public class CimJsonExporterTests : CimTestFixture
	{
		[TestMethod]
		public void ASolutionIsExportedCorrectly()
		{
			CimSolution cimSolution = CreateCimSolution();

			// (the CimJsonExporter under test is used per-period by the CimSolutionConverter)
			var converter = new CimSolutionConverter(_networkConverter);

			// In test, use a fixed graph GUID and time to produce a predictable result
			var metadata = new CimJsonExporter.SolutionMetadata
			{
				GraphGuid = new Guid("9f031e32-66e7-4ae3-ad69-bda4d69d51ac"),
				GeneratedAtTime = new DateTime(2022, 9, 6)
			};

			var solutionAsJObject = converter.ConvertToCimJsonLd(cimSolution, new[] { metadata });

			TestUtils.AssertValidDiginSolution(solutionAsJObject, requireExact: true);
		}

		/// <summary>
		/// Returns a CIM solution for the DIGIN case
		/// </summary>
		private CimSolution CreateCimSolution()
		{
			ConvertNetwork();
			ReadAndConvertDiginDemands();
			CreateEncodingAndSolution();

			// Convert solution to CIM
			var converter = new CimSolutionConverter(_networkConverter);
			return converter.ConvertToCim(_disaggregatedSolution);
		}
	}
}