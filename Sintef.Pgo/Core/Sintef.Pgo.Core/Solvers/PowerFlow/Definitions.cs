namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A type of flow approximation
	/// </summary>
	public enum FlowApproximation
	{
		/// <summary>
		/// Flow calculation by direct-current approximation.
		/// </summary>
		DC = 0,

		/// <summary>
		/// Solves dist-flow equations by assuming that power demand is not dependent on voltage loss. Equivalent to IteratedDF with iteration limit 1.
		/// </summary>
		SimplifiedDF,

		/// <summary>
		/// Flow calculation by Taylor approximation.
		/// </summary>
		Taylor,

		/// <summary>
		/// Solves dist-flow equations using an iterative method.
		/// </summary>
		IteratedDF,

		/// <summary>
		/// Undefined (error value)
		/// </summary>
		UNDEFINED
	}

}