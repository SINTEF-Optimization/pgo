namespace Sintef.Pgo.REST.Tests
{
	internal class JsonSessionInformation
	{
		public string Id { get; }
		public string ProblemContent { get; }
		public string ConfigurationContent { get; }

		public JsonSessionInformation(
			string id,
			string problemContent,
			string configurationContent)
		{
			Id = id;
			ProblemContent = problemContent;
			ConfigurationContent = configurationContent;
		}
	}
}