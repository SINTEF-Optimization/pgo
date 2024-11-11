using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for solution handling functionality in a session in the .NET API
	/// </summary>
	[TestClass]
	public class SolutionHandlingTests : ApiTestFixture
	{
		private ISession _jsonSession = null!;
		private ISession _cimSession = null!;
		private ISession _cimJsonLdSession = null!;

		[TestInitialize]
		public new void Setup()
		{
			base.Setup();

			AddJsonTestNetwork();
			AddCimTestNetwork();
			AddCimJsonLdTestNetwork();

			_jsonSession = AddJsonTestSession();
			_cimSession = AddCimTestSession();
			_cimJsonLdSession = AddCimJsonLdTestSession();
		}

		[TestMethod]
		public void NewSessionHasStartConfigurationAsBestSolution()
		{
			AssertSolutionIds(_jsonSession, "best");
			AssertSolutionIds(_cimSession, "best");

			var jsonStartConfig = JsonTestData().SinglePeriodSettings;
			var jsonSolution = _jsonSession.BestJsonSolution;

			Assert.AreEqual(Describe(jsonStartConfig), Describe(jsonSolution.PeriodSettings[0]));

			var cimStartConfig = CimTestData().Configuration;
			var cimSolution = _cimSession.BestCimSolution;

			Assert.AreEqual(Describe(cimStartConfig), Describe(cimSolution.PeriodSolutions[0]));
		}

		[TestMethod]
		public void NewSessionWithoutStartConfigurationHasAllClosedSolution()
		{
			var jsonSession = AddJsonTestSession("id1", omitStartConfig: true);
			var cimSession = AddCimTestSession("id2", omitStartConfig: true);

			AssertSolutionIds(jsonSession, "best");
			AssertSolutionIds(cimSession, "best");

			var jsonSolution = jsonSession.BestJsonSolution;

			Assert.AreEqual("Closed switches: L1 L2", Describe(jsonSolution.PeriodSettings[0]));

			var cimSolution = cimSession.BestCimSolution;

			Assert.AreEqual("Closed switches: switch", Describe(cimSolution.PeriodSolutions[0]));
		}

		[TestMethod]
		public void SolutionsCanBeAddedAndRemoved()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());
			_cimSession.AddSolution("aa", CimTestSolution());

			AssertSolutionIds(_jsonSession, "aa best");
			AssertSolutionIds(_cimSession, "aa best");

			_jsonSession.AddSolution("bb", JsonTestSolution());
			_cimSession.AddSolution("bb", CimTestSolution());

			AssertSolutionIds(_jsonSession, "aa bb best");
			AssertSolutionIds(_cimSession, "aa bb best");

			_jsonSession.RemoveSolution("aa");
			_cimSession.RemoveSolution("aa");

			AssertSolutionIds(_jsonSession, "bb best");
			AssertSolutionIds(_cimSession, "bb best");
		}

		[TestMethod]
		public void BestSolutionCannotBeAdded()
		{
			TestUtils.AssertException(() => _jsonSession.AddSolution("best", JsonTestSolution()), "The solution ID 'best' is reserved for the best known solution");
			TestUtils.AssertException(() => _cimSession.AddSolution("best", CimTestSolution()), "The solution ID 'best' is reserved for the best known solution");
		}

		[TestMethod]
		public void ASolutionWithTheSameIdCannotBeAdded()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());
			_cimSession.AddSolution("aa", CimTestSolution());
			_cimJsonLdSession.AddSolution("aa", _cimJsonLdSession.BestCimJsonLdSolution);

			TestUtils.AssertException(() => _jsonSession.AddSolution("aa", JsonTestSolution()), "There is already a solution with ID 'aa'");
			TestUtils.AssertException(() => _cimSession.AddSolution("aa", CimTestSolution()), "There is already a solution with ID 'aa'");
			TestUtils.AssertException(() => _cimJsonLdSession.AddSolution("aa", _cimJsonLdSession.BestCimJsonLdSolution), "There is already a solution with ID 'aa'");
		}

		[TestMethod]
		public void ASolutionCanBeRetrievedAfterAdding()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());
			_cimSession.AddSolution("aa", CimTestSolution());

			var jsonSolution = _jsonSession.GetJsonSolution("aa");
			var cimSolution = _cimSession.GetCimSolution("aa");

			AssertPlausibleSolution(jsonSolution);
			AssertPlausibleSolution(cimSolution);
		}

		[TestMethod]
		public void UnknownSolutionCannotBeRemoved()
		{
			TestUtils.AssertException(() => _jsonSession.RemoveSolution("aa"), "There is no solution with ID 'aa'");
			TestUtils.AssertException(() => _cimSession.RemoveSolution("aa"), "There is no solution with ID 'aa'");
		}

		[TestMethod]
		public void SolutionInPoolCanBeUpdated_Json()
		{
			// Add a solution with all switches closed
			var solution = JsonTestSolution();
			DoForEachSwitch(solution, s => s.Open = false);

			_jsonSession.AddSolution("aa", solution);

			// Open all switches and update
			DoForEachSwitch(solution, s => s.Open = true);

			_jsonSession.UpdateSolution("aa", solution);

			var updatedSolution = _jsonSession.GetJsonSolution("aa");

			DoForEachSwitch(solution, s => Assert.IsTrue(s.Open));
		}

		[TestMethod]
		public void SolutionInPoolCanBeUpdated_Cim()
		{
			// Add a solution with all switches closed
			var solution = CimTestSolution();
			DoForEachSwitch(solution, s => s.Open = false);

			_cimSession.AddSolution("aa", solution);

			// Open all switches and update
			DoForEachSwitch(solution, s => s.Open = true);

			_cimSession.UpdateSolution("aa", solution);

			var updatedSolution = _cimSession.GetCimSolution("aa");

			DoForEachSwitch(solution, s => Assert.IsTrue(s.Open));
		}

		[TestMethod]
		public void ErrorInSolutionIsReported_Json()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());

			var badSolution = JsonTestSolution();
			badSolution.PeriodSettings[0].SwitchSettings.Add(null);

			TestUtils.AssertException(() => _jsonSession.AddSolution("bb", badSolution),
				"Period '0': The list of switch settings contains null", requiredType: typeof(ArgumentException));
			TestUtils.AssertException(() => _jsonSession.UpdateSolution("aa", badSolution),
				"Period '0': The list of switch settings contains null", requiredType: typeof(ArgumentException));
		}

		[TestMethod]
		public void ErrorInSolutionIsReported_Cim()
		{
			_cimSession.AddSolution("aa", CimTestSolution());

			var badSolution = CimTestSolution();
			badSolution.PeriodSolutions[0].Switches.Add(null);

			TestUtils.AssertException(() => _cimSession.AddSolution("bb", badSolution), "Period 'Period 1': The list of switches contains null");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("aa", badSolution), "Period 'Period 1': The list of switches contains null");
		}

		[TestMethod]
		public void BestSolutionCannotBeRemoved()
		{
			TestUtils.AssertException(() => _jsonSession.RemoveSolution("best"), "Cannot remove the 'best' solution");
			TestUtils.AssertException(() => _cimSession.RemoveSolution("best"), "Cannot remove the 'best' solution");
		}

		[TestMethod]
		public void BestSolutionCannotBeUpdated()
		{
			TestUtils.AssertException(() => _jsonSession.UpdateSolution("best", JsonTestSolution()), "The solution with ID 'best' can not be updated");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("best", CimTestSolution()), "The solution with ID 'best' can not be updated");
		}

		[TestMethod]
		public void AddingASolutionDoesNotUpdateTheBestSolution()
		{
			// The initial best solution has all switches closed, which is infeasible
			var jsonSession = AddJsonTestSession("id1", omitStartConfig: true);

			Assert.IsFalse(jsonSession.BestSolutionInfo.IsFeasible);

			// Add a feasible solution
			jsonSession.AddSolution("solution", JsonTestSolution());
			Assert.IsTrue(jsonSession.GetSolutionInfo("solution").IsFeasible);

			// The best is still infeasible. We might improve this by updating the best solution.
			Assert.IsFalse(jsonSession.BestSolutionInfo.IsFeasible);
		}

		[TestMethod]
		public void AccessingNonexistentSolutionFails()
		{
			TestUtils.AssertException(() => _jsonSession.GetJsonSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _jsonSession.RemoveSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _jsonSession.GetSolutionInfo("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _jsonSession.RepairSolution("??", ""), "There is no solution with ID '??'");

			TestUtils.AssertException(() => _cimSession.GetCimSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimSession.RemoveSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimSession.GetSolutionInfo("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimSession.RepairSolution("??", ""), "There is no solution with ID '??'");

			TestUtils.AssertException(() => _cimJsonLdSession.GetCimJsonLdSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimJsonLdSession.RemoveSolution("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimJsonLdSession.GetSolutionInfo("??"), "There is no solution with ID '??'");
			TestUtils.AssertException(() => _cimJsonLdSession.RepairSolution("??", ""), "There is no solution with ID '??'");
		}

		[TestMethod]
		public void UsingWrongSolutionTypeFails()
		{
			var cimJsonLdSolution = _cimJsonLdSession.BestCimJsonLdSolution;

			TestUtils.AssertException(() => _jsonSession.GetCimSolution("best"), "This function can not be used in a session based on JSON data");
			TestUtils.AssertException(() => _jsonSession.AddSolution("bb", CimTestSolution()), "This function can not be used in a session based on JSON data");
			TestUtils.AssertException(() => _jsonSession.UpdateSolution("bb", CimTestSolution()), "This function can not be used in a session based on JSON data");

			TestUtils.AssertException(() => _jsonSession.GetCimJsonLdSolution("best"), "This function can not be used in a session based on JSON data");
			TestUtils.AssertException(() => _jsonSession.AddSolution("bb", cimJsonLdSolution), "This function can not be used in a session based on JSON data");
			TestUtils.AssertException(() => _jsonSession.UpdateSolution("bb", cimJsonLdSolution), "This function can not be used in a session based on JSON data");


			TestUtils.AssertException(() => _cimSession.GetJsonSolution("best"), "This function can not be used in a session based on CIM data");
			TestUtils.AssertException(() => _cimSession.AddSolution("bb", JsonTestSolution()), "This function can not be used in a session based on CIM data");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("bb", JsonTestSolution()), "This function can not be used in a session based on CIM data");
			
			TestUtils.AssertException(() => _cimSession.GetCimJsonLdSolution("best"), "Cannot export solution to JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");
			TestUtils.AssertException(() => _cimSession.AddSolution("bb", cimJsonLdSolution), "Cannot create a solution from JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("bb", cimJsonLdSolution), "Cannot create a solution from JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");


			TestUtils.AssertException(() => _cimJsonLdSession.GetJsonSolution("best"), "This function can not be used in a session based on CIM data");
			TestUtils.AssertException(() => _cimJsonLdSession.AddSolution("bb", JsonTestSolution()), "This function can not be used in a session based on CIM data");
			TestUtils.AssertException(() => _cimJsonLdSession.UpdateSolution("bb", JsonTestSolution()), "This function can not be used in a session based on CIM data");

			// These are ok
			var cimSolution = _cimJsonLdSession.GetCimSolution("best");
			_cimJsonLdSession.AddSolution("bb", cimSolution);
			_cimJsonLdSession.UpdateSolution("bb", cimSolution);
			
		}

		[TestMethod]
		public void InfoForASolutionCanBeRetrieved()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());
			SolutionInfo info = _jsonSession.GetSolutionInfo("aa");

			Assert.IsTrue(info.IsFeasible);
			Assert.IsFalse(info.IsOptimal);
			Assert.AreEqual(0, info.ObjectiveValue, 1e-3);
			Assert.AreEqual(5, info.ObjectiveComponents.Count);
			Assert.AreEqual(1, info.PeriodInformation.Count);
			Assert.AreEqual(0, info.ViolatedConstraints.Count);

			var periodInfo = info.PeriodInformation[0];
			Assert.AreEqual(0, periodInfo.ChangedSwitches);
			Assert.AreEqual("0", periodInfo.Period.Id);
		}

		[TestMethod]
		public void UnconnectedSolutionCanBeRepaired()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1[open] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]",
				"Gen1 -- Line3[open] -- X2 -- Line4[closed] -- Consumer");

			ISession session = AddNetworkAndSession(builder, solutionId: "aa");

			var info = session.GetSolutionInfo("aa");
			Assert.AreEqual("These consumers are unconnected and have a demand of 1.00 VA active power (100.00 % of total active power demand) in period 0: Consumer",
				info.ViolatedConstraints.Single().Description);

			var newId = "repaired";
			var repairMessage = session.RepairSolution("aa", newId);

			Assert.AreEqual("Repair was successful after opening 0 and closing 1 switches.", repairMessage);

			info = session.GetSolutionInfo(newId);
			var solution = session.GetJsonSolution(newId);
			Assert.IsTrue(info.IsFeasible);
		}

		[TestMethod]
		public void RepairFailsIfSolutionIdExists()
		{
			_jsonSession.AddSolution("aa", JsonTestSolution());

			TestUtils.AssertException(() => _jsonSession.RepairSolution("aa", "aa"), "There is already a solution with ID 'aa'");
			TestUtils.AssertException(() => _jsonSession.RepairSolution("aa", "best"), "The solution ID 'best' is reserved for the best known solution");
		}

		[TestMethod]
		public void NullArgumentsAreCaught()
		{
			TestUtils.AssertException(() => _jsonSession.AddSolution(null!, new Solution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _jsonSession.AddSolution("", (Solution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _cimSession.AddSolution(null!, new CimSolution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.AddSolution("", (CimSolution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _cimSession.AddSolution(null!, new CimJsonLdSolution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.AddSolution("", (CimJsonLdSolution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _jsonSession.UpdateSolution(null!, new Solution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _jsonSession.UpdateSolution("", (Solution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _cimSession.UpdateSolution(null!, new CimSolution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("", (CimSolution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _cimSession.UpdateSolution(null!, new CimJsonLdSolution()), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.UpdateSolution("", (CimJsonLdSolution)null!), "Value cannot be null. (Parameter 'solution')");

			TestUtils.AssertException(() => _jsonSession.GetJsonSolution(null!), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.GetCimSolution(null!), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _cimSession.GetCimJsonLdSolution(null!), "Value cannot be null. (Parameter 'id')");

			TestUtils.AssertException(() => _jsonSession.RemoveSolution(null!), "Value cannot be null. (Parameter 'id')");

			TestUtils.AssertException(() => _jsonSession.GetSolutionInfo(null!), "Value cannot be null. (Parameter 'id')");

			TestUtils.AssertException(() => _jsonSession.RepairSolution(null!, ""), "Value cannot be null. (Parameter 'id')");
			TestUtils.AssertException(() => _jsonSession.RepairSolution("", null!), "Value cannot be null. (Parameter 'newId')");
		}

		/// <summary>
		/// Verifies that the given JSON solution seems to contain a full single period solution
		/// </summary>
		private void AssertPlausibleSolution(Solution solution)
		{
			Assert.AreEqual(1, solution.PeriodSettings.Count);
			Assert.AreEqual(1, solution.Flows.Count);
			Assert.AreEqual(1, solution.KileCosts.Count);
		}

		/// <summary>
		/// Verifies that the given CIM solution seems to contain a full single period solution
		/// </summary>
		private void AssertPlausibleSolution(CimSolution cimSolution)
		{
			Assert.AreEqual(1, cimSolution.PeriodSolutions.Count);
			Assert.IsTrue(cimSolution.PeriodSolutions[0].Switches.Count > 0);
		}

		/// <summary>
		/// Creates a CIM solution suitable for the default CIM session
		/// </summary>
		private CimSolution CimTestSolution()
		{
			return new CimSolution()
			{
				PeriodSolutions = new() { CimPeriodSolution.FromObjects(CimTestData().Configuration.Switches) }
			};
		}

		/// <summary>
		/// Creates a JSON solution suitable for the default JSON session
		/// </summary>
		private Solution JsonTestSolution()
		{
			return new Solution()
			{
				PeriodSettings = new() { JsonTestData().SinglePeriodSettings }
			};
		}

		/// <summary>
		/// Performs the given action for each swich (and period) in the given solution
		/// </summary>
		private void DoForEachSwitch(CimSolution solution, Action<Cim.Switch> action)
		{
			solution.PeriodSolutions
				.SelectMany(s => s.Switches)
				.Do(action);
		}

		/// <summary>
		/// Performs the given action for each swich (and period) in the given solution
		/// </summary>
		private static void DoForEachSwitch(Solution solution, Action<SwitchState> action)
		{
			solution.PeriodSettings
				.SelectMany(s => s.SwitchSettings)
				.Do(action);
		}

		/// <summary>
		/// Returns a text description of the given period solution
		/// </summary>
		private string Describe(CimPeriodSolution cimPeriodSolution)
		{
			return Describe(cimPeriodSolution.Switches.Select(x => (x.MRID, x.Open!.Value)));
		}

		/// <summary>
		/// Returns a text description of the given configuration
		/// </summary>
		private string Describe(CimConfiguration cimStartConfig)
		{
			return Describe(cimStartConfig.Switches.Select(x => (x.MRID, x.Open!.Value)));
		}

		/// <summary>
		/// Returns a text description of the given configuration
		/// </summary>
		private string Describe(SinglePeriodSettings settings)
		{
			return Describe(settings.SwitchSettings.Select(x => (x.Id, x.Open)));
		}

		/// <summary>
		/// Returns a text description of the given switch settings
		/// </summary>
		private static string Describe(IEnumerable<(string Id, bool Open)> switchSettings)
		{
			var open = switchSettings.Where(s => s.Open);
			var closed = switchSettings.Where(s => !s.Open);

			List<string> result = new();

			if (open.Any())
				result.Add("Open switches: " + open.OrderBy(s => s.Id).Select(s => s.Id).Join(" "));
			if (closed.Any())
				result.Add("Closed switches: " + closed.OrderBy(s => s.Id).Select(s => s.Id).Join(" "));

			return result.Join(", ");
		}
	}
}
