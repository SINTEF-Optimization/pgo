using Newtonsoft.Json;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// An objective component, and its associated weight.
	/// </summary>
	public class ObjectiveWeight
	{
		/// <summary>
		/// The name of the objective component
		/// </summary>
		public string ObjectiveName { get; set; }

		/// <summary>
		/// The weight of the component in the full objective
		/// </summary>
		public double Weight { get; set; }
	}
}
