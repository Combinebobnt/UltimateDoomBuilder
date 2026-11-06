using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	public class DockersExpandTargetTests
	{
		[Fact]
		public void StoredDockerPresentIsSelected()
		{
			List<string> keys = new List<string> { "undo", "scripts", "drawsettings", "Help" };
			Assert.Equal(3, DockersControl.FindExpandTarget(keys, "Help", "scripts"));
		}

		[Fact]
		public void StoredDockerRemovedWhileCollapsedFallsBackToPrevious()
		{
			List<string> keys = new List<string> { "undo", "scripts", "drawsettings" };
			Assert.Equal(1, DockersControl.FindExpandTarget(keys, "drawgrid", "scripts"));
		}

		[Fact]
		public void StoredAndPreviousGoneFallsBackToFirstPage()
		{
			List<string> keys = new List<string> { "undo", "scripts" };
			Assert.Equal(0, DockersControl.FindExpandTarget(keys, "drawgrid", "comments"));
			Assert.Equal(0, DockersControl.FindExpandTarget(keys, null, null));
		}

		[Fact]
		public void EarlierPageRemovedShiftsIndex()
		{
			// "comments" was index 4 before "drawsettings" was removed ahead of it
			List<string> keys = new List<string> { "undo", "scripts", "tags", "comments", "Help" };
			Assert.Equal(3, DockersControl.FindExpandTarget(keys, "comments", "undo"));
		}

		[Fact]
		public void EmptyListSelectsNothing()
		{
			Assert.Equal(-1, DockersControl.FindExpandTarget(new List<string>(), "comments", "undo"));
		}
	}
}
