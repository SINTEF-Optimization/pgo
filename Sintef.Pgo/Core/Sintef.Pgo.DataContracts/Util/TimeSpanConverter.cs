using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Newtonsoft.Json;

namespace Sintef.Pgo.DataContracts
{
        
	/// <summary>
	/// Converter for TimeSpans that understands ISO 8601
	/// </summary>
	public class TimeSpanConverter : JsonConverter<TimeSpan?>
	{
		/// <summary>
		/// Converts a json string to TimeSpan
		/// </summary>
		public override TimeSpan? ReadJson(JsonReader reader, Type objectType, TimeSpan? existingValue, bool has, JsonSerializer serializer)
		{
			if (objectType != typeof(TimeSpan?) && objectType != typeof(TimeSpan))
				throw new ArgumentException();

			if (reader.Value is string spanString)
				return XmlConvert.ToTimeSpan(spanString);

			return null;
		}

		/// <summary>
		/// Converts a TimeSpan to a json string
		/// </summary>
		public override void WriteJson(JsonWriter writer, TimeSpan? value, JsonSerializer serializer)
		{
			var duration = (TimeSpan)value;
			writer.WriteValue(XmlConvert.ToString(duration));
		}
	}
}
