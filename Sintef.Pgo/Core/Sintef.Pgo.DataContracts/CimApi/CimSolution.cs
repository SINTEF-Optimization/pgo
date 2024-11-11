using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// A representation of a PGO solution, as CIM data on JSON-LD format
	/// </summary>
	public class CimJsonLdSolution
	{
		/// <summary>
		/// Solution information for each period in the problem. 
		/// 
		/// The information for each period is represented as a JSON-LD graph object 
		/// containing a collection of CIM objects. For details about which CIM objects and properties
		/// to include, see <see cref="CimPeriodSolution"/>.
		/// </summary>
		public List<JObject> PeriodSolutions { get; set; } = new();
	}

	/// <summary>
	/// A representation of a PGO solution, as CIM objects
	/// </summary>
	public class CimSolution
	{
		/// <summary>
		/// Solution information for each period in the problem
		/// </summary>
		public List<CimPeriodSolution> PeriodSolutions { get; set; } = new();
	}

	/// <summary>
	/// A collection of CIM objects that define a PGO solution for one period
	/// 
	/// These objects do not need to define complete CIM objects, but only supply certain properties of CIM objects whose
	/// primary definition is in the network data. The referenced network object is identified as follows:
	///  - If using CIM objects directly in the .NET API, the MRID property MUST be set and identifies the network object.
	///  - If using JSON-LD (through the REST API or .NET API), the MRID property MAY be set to identify the network object.
	///    If it is not, the network object is identified by URI.
	/// </summary>
	public class CimPeriodSolution
	{
		/// <summary>
		/// Data for the network's switches.
		/// 
		/// The following properties are relevant to the solution: 
		///  - 'open' contains the state of the switch
		/// </summary>
		public List<Switch> Switches { get; set; } = new();

		/// <summary>
		/// Creates a <see cref="CimPeriodSolution"/> by collecting the relevant CIM objects in <paramref name="cimObjects"/>
		/// </summary>
		public static CimPeriodSolution FromObjects(IEnumerable<IdentifiedObject> cimObjects)
		{
			return new CimPeriodSolution
			{
				Switches = cimObjects.OfType<Switch>().ToList()
			};
		}
	}
}