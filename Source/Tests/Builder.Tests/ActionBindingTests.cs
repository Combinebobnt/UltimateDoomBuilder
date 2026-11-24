using System.Collections.Generic;
using System.Reflection;
using CodeImp.DoomBuilder.Actions;
using Xunit;

namespace Builder.Tests
{
	public class ActionBindingTests
	{
		private class ModeBase
		{
			[BeginAction("zoomin")]
			[EndAction("zoomin")]
			public virtual void ZoomIn() { }
		}

		private class ModeMiddle : ModeBase { }

		private class ModeLeaf : ModeMiddle { }

		private class ModeOverride : ModeMiddle
		{
			public override void ZoomIn() { }
		}

		private class ModeOverrideOwn : ModeMiddle
		{
			[BeginAction("zoomout")]
			public override void ZoomIn() { }
		}

		private class ModeHiding : ModeBase
		{
			private new void ZoomIn() { }
		}

		private class ModeOverridePastHidden : ModeHiding
		{
			[BeginAction("zoomout")]
			public override void ZoomIn() { }
		}

		private static List<string> Names(System.Type type, System.Type attributetype)
		{
			MethodInfo m = type.GetMethod("ZoomIn", BindingFlags.Instance | BindingFlags.Public);
			return ActionManager.GetActionNames(m, attributetype, typeof(ActionBindingTests).Assembly);
		}

		private static string Full(string action)
		{
			return typeof(ActionBindingTests).Assembly.GetName().Name.ToLowerInvariant() + "_" + action;
		}

		// Mono returned the base's attribute twice through a subclass, so one wheel notch zoomed two steps
		[Fact]
		public void InheritedVirtualActionMethodBindsEachActionOnce()
		{
			System.Type[] types = { typeof(ModeBase), typeof(ModeMiddle), typeof(ModeLeaf), typeof(ModeOverride) };
			foreach(System.Type type in types)
			{
				Assert.Equal(new List<string> { Full("zoomin") }, Names(type, typeof(BeginActionAttribute)));
				Assert.Equal(new List<string> { Full("zoomin") }, Names(type, typeof(EndActionAttribute)));
			}
		}

		[Fact]
		public void OverrideKeepsItsOwnActionAndTheInheritedOne()
		{
			foreach(System.Type type in new[] { typeof(ModeOverrideOwn), typeof(ModeOverridePastHidden) })
			{
				List<string> names = Names(type, typeof(BeginActionAttribute));
				names.Sort();
				Assert.Equal(new List<string> { Full("zoomin"), Full("zoomout") }, names);
			}
		}
	}
}
