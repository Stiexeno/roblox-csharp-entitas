namespace Entitas
{
	// `Feature` is `Systems` with a name. Frozen-feast composes a tree of
	// features (`new GameplayFeature(contexts).Add(new CombatFeature(...))`)
	// to organize ECS code by domain. The named identity is only used for
	// debugging; runtime behavior matches Systems exactly.
	public class Feature : Systems
	{
		public string name { get; }

		public Feature(string name)
		{
			this.name = name;
		}

		public Feature() : this("Feature") { }
	}
}
