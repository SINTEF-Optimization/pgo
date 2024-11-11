namespace Sintef.Pgo.REST.Tests
{
	internal class CIMSessionInformation
	{
		public string Id { get; }
		public string ProblemName { get; }
		public string PowerDemands { get; }
		public string CurrentConfiguration { get; }

		public CIMSessionInformation(
			string id,
			string problemName,
			string powerDemandsContent,
			string currentConfigurationContent)
		{
			Id = id;
			ProblemName = problemName;
			PowerDemands = powerDemandsContent;
			CurrentConfiguration = currentConfigurationContent;
		}
	}
}
