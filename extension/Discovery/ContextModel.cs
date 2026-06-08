using System.Collections.Generic;

namespace RobloxCSharp.Extensions.Entities
{
	internal sealed class ContextModel
	{
		public string Name { get; }
		public List<ComponentModel> Components { get; } = new();

		public ContextModel(string name) { Name = name; }
	}
}
