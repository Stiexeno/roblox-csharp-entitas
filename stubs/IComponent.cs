namespace Entitas
{
	// Marker for component types. An entity holds at most one component
	// per concrete IComponent subtype; components carry the data, systems
	// carry the behavior. Match Entitas 1.14's contract exactly so
	// frozen-feast-style sources compile unchanged.
	public interface IComponent
	{
	}
}
