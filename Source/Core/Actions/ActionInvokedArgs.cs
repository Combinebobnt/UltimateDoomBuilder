
namespace CodeImp.DoomBuilder.Actions
{
	internal delegate void ActionInvokedHandler(ActionInvokedArgs e);

	// One action invocation from user input, as macro recording sees it
	internal sealed class ActionInvokedArgs
	{
		public readonly Action Action;

		// The key, mouse button or scroll code that began the action, with modifiers; 0 for a menu, button or palette
		public readonly int Key;

		// Action Begin/End calls already running when this one started
		public readonly int DepthAtEntry;

		public readonly bool VolatileBefore;

		public ActionInvokedArgs(Action action, int key, int depthatentry, bool volatilebefore)
		{
			Action = action;
			Key = key;
			DepthAtEntry = depthatentry;
			VolatileBefore = volatilebefore;
		}
	}
}
