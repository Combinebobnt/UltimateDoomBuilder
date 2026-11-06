using CodeImp.DoomBuilder.Data;

namespace Builder.Tests
{
	public class SpriteMergeTests
	{
		// Containers in load order, IWAD first
		private static HashSet<string> Merge(params string[][] containers)
		{
			return DataManager.MergeSpriteNames(containers.Select(c => (IEnumerable<string>)c).ToList());
		}

		[Fact]
		public void PartialModSet_ReplacesOnlyItsOwnFrames()
		{
			HashSet<string> result = Merge(new[] { "PLAYA1", "PLAYA2A8", "PLAYW0", "TFOGA0", "TFOGB0" }, new[] { "PLAYW0", "PLAYG2G8", "TFOGI0" });
			foreach(string name in new[] { "PLAYA1", "PLAYA2A8", "TFOGB0", "TFOGI0", "PLAYW0", "PLAYG2G8" })
				Assert.True(result.Contains(name), name);
		}

		[Fact]
		public void FewerRotationsInTheMod_DropTheIwadRotationsOfThatFrame()
		{
			HashSet<string> result = Merge(new[] { "POSSA1", "POSSA2A8", "POSSB1" }, new[] { "POSSA0" });
			Assert.True(result.Contains("POSSA0"), "POSSA0");
			Assert.True(result.Contains("POSSB1"), "POSSB1");
			Assert.False(result.Contains("POSSA1"), "POSSA1");
			Assert.False(result.Contains("POSSA2A8"), "POSSA2A8");
		}

		[Fact]
		public void OneContainer_KeepsAllItsRotationsOfAFrame()
		{
			HashSet<string> result = Merge(new[] { "TROOA1", "TROOA2A8", "TROOA3A7" });
			Assert.Equal(3, result.Count);
		}

		// Pins UDB's per-frame approximation: the engine fills per rotation and would keep SARGA2A8 for rotations 2 and 8
		[Fact]
		public void MirrorNameWhoseOnlyFrameIsClaimed_IsDroppedPerFrame()
		{
			HashSet<string> result = Merge(new[] { "SARGA2A8" }, new[] { "SARGA1" });
			Assert.True(result.Contains("SARGA1"), "SARGA1");
			Assert.False(result.Contains("SARGA2A8"), "SARGA2A8");
		}

		[Fact]
		public void ContainerWithNoSprites_IsSkipped()
		{
			var containers = new List<IEnumerable<string>> { new[] { "PLAYA1" }, null! };
			Assert.True(DataManager.MergeSpriteNames(containers).Contains("PLAYA1"), "PLAYA1");
		}
	}
}
