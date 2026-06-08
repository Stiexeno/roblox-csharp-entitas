namespace RobloxCSharp.Extensions.Entities
{
	internal enum FieldIndexKind { None, Index, PrimaryIndex }

	internal readonly struct ComponentField
	{
		public string Name { get; }
		public string TypeFullName { get; }
		public FieldIndexKind IndexKind { get; }
		public ComponentField(string name, string typeFullName, FieldIndexKind indexKind = FieldIndexKind.None)
		{
			Name = name; TypeFullName = typeFullName; IndexKind = indexKind;
		}
		public bool IsIndexed => IndexKind != FieldIndexKind.None;
		public bool IsPrimaryIndexed => IndexKind == FieldIndexKind.PrimaryIndex;
	}
}
