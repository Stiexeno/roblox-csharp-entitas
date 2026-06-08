namespace Entities
{
	// Codegen-emitted `Contexts` class implements this. `allContexts`
	// powers blanket lifecycle calls (Reset everything, etc.) without the
	// caller having to know which contexts exist.
	public interface IContexts
	{
		IContext[] allContexts { get; }

		void Reset();
	}
}
