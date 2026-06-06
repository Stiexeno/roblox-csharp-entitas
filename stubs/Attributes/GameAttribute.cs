namespace Entitas.CodeGeneration.Attributes
{
	// Routes the marked IComponent into the Game context. Mirrors
	// frozen-feast's conventional [Game] tag verbatim. New contexts are
	// added by subclassing ContextAttribute the same way (see
	// InputAttribute, InventoryAttribute, etc.).
	public class GameAttribute : ContextAttribute
	{
		public GameAttribute() : base("Game") { }
	}
}
