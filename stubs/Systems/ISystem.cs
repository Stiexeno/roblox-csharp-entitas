namespace Entitas
{
	// Empty root interface; lets Feature.Add accept anything system-shaped
	// and stash it into the right bucket (initialize / execute / cleanup
	// / teardown) by runtime cast.
	public interface ISystem
	{
	}
}
