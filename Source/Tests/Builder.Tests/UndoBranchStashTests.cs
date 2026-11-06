using CodeImp.DoomBuilder.Editing;

namespace Builder.Tests
{
	// A depth miscount swaps the wrong levels in on restore, which is itself data loss
	public class UndoBranchStashTests
	{
		// A linear history driven the way UndoManager drives the stash: "applied" is the map, oldest first
		private class History
		{
			public readonly List<string> applied = new List<string>();
			public readonly List<string> redos = new List<string>(); // index 0 is the next redo
			public readonly UndoBranchStash<string> branch = new UndoBranchStash<string>();

			public void Edit(string level)
			{
				if(redos.Count > 0)
				{
					if(!branch.KeepsOver(redos.Count)) branch.Stash(redos.ToArray());
					redos.Clear();
				}
				applied.Add(level);
				branch.LevelCreated();
			}

			public void Undo(int n)
			{
				for(int i = 0; i < n && applied.Count > 0; i++)
				{
					redos.Insert(0, applied[applied.Count - 1]);
					applied.RemoveAt(applied.Count - 1);
					branch.LevelUndone();
				}
			}

			public void Redo(int n)
			{
				for(int i = 0; i < n && redos.Count > 0; i++)
				{
					applied.Add(redos[0]);
					redos.RemoveAt(0);
					branch.LevelRedone();
				}
			}

			public void Restore()
			{
				Assert.True(branch.IsValid(applied.Count, redos.Count), "branch point reachable");
				int depth = branch.Depth;
				if(depth > 0) Undo(depth); else Redo(-depth);
				List<string> older = branch.Swap(redos.ToArray());
				redos.Clear();
				redos.AddRange(older);
				Redo(redos.Count);
			}

			public string Map { get { return string.Join(",", applied); } }
		}

		[Fact]
		public void RestoreAfterEditingOnTopOfAnUndoSwapsTheLinesAndToggles()
		{
			History h = new History();
			h.Edit("A"); h.Edit("B"); h.Edit("C");
			h.Undo(2);
			h.Edit("D"); h.Edit("E");
			Assert.Equal(2, h.branch.Depth);

			h.Restore();
			Assert.Equal("A,B,C", h.Map);
			Assert.Equal(new[] { "D", "E" }, h.branch.ToArrayForTest());
			Assert.Equal(2, h.branch.Depth);

			h.Restore();
			Assert.Equal("A,D,E", h.Map);
			Assert.Equal(new[] { "B", "C" }, h.branch.ToArrayForTest());
		}

		[Fact]
		public void RestoreFromBelowAndAboveTheBranchPointLandsTheSame()
		{
			// Below: the common level A is undone too, so restore has to redo back up first
			History below = new History();
			below.Edit("A"); below.Edit("B"); below.Undo(1); below.Edit("C");
			below.Undo(2);
			Assert.Equal(-1, below.branch.Depth);
			below.Restore();
			Assert.Equal("A,B", below.Map);

			// Above, with undo and redo runs in between
			History above = new History();
			above.Edit("A"); above.Edit("B"); above.Undo(1); above.Edit("C"); above.Edit("D");
			above.Undo(2); above.Redo(1);
			Assert.Equal(1, above.branch.Depth);
			above.Restore();
			Assert.Equal("A,B", above.Map);
			Assert.Equal(new[] { "C", "D" }, above.branch.ToArrayForTest());
		}

		[Fact]
		public void NewDiscardReplacesThePreviousBranch()
		{
			UndoBranchStash<string> b = new UndoBranchStash<string>();
			b.Stash(new[] { "B", "C" });
			b.LevelCreated(); b.LevelCreated();
			List<string> replaced = b.Stash(new[] { "X" });
			Assert.Equal(new[] { "B", "C" }, replaced.ToArray());
			Assert.Equal(new[] { "X" }, b.ToArrayForTest());
			Assert.Equal(0, b.Depth);
		}

		[Fact]
		public void OrdinaryUndoThenEditKeepsALargerReachableBranch()
		{
			History h = new History();
			h.Edit("A"); h.Edit("B"); h.Edit("C"); h.Edit("D");
			h.Undo(3);
			h.Edit("X"); h.Edit("Y");
			h.Undo(1);
			h.Edit("Z");
			Assert.Equal(new[] { "B", "C", "D" }, h.branch.ToArrayForTest());
			Assert.Equal(2, h.branch.Depth);
			h.Restore();
			Assert.Equal("A,B,C,D", h.Map);
		}

		[Fact]
		public void LargerDiscardReplacesASmallerBranch()
		{
			History h = new History();
			h.Edit("A"); h.Edit("B"); h.Undo(1); h.Edit("C");
			h.Edit("D"); h.Edit("E");
			h.Undo(3);
			h.Edit("X");
			Assert.Equal(new[] { "C", "D", "E" }, h.branch.ToArrayForTest());
			h.Restore();
			Assert.Equal("A,C,D,E", h.Map);
		}

		[Fact]
		public void EditBelowTheBranchPointReplacesTheBranchWithTheCommonLevels()
		{
			History h = new History();
			h.Edit("A"); h.Edit("B"); h.Undo(1); h.Edit("C");
			h.Undo(2);
			Assert.Equal(-1, h.branch.Depth);
			h.Edit("X");
			Assert.Equal(new[] { "A", "C" }, h.branch.ToArrayForTest());
			Assert.Equal(1, h.branch.Depth);
			h.Restore();
			Assert.Equal("A,C", h.Map);
		}

		[Fact]
		public void EditAfterTheCommonRedoWasDisposedInvalidatesTheBranch()
		{
			// A withdrawn undo below the branch point disposes the common level's redo, leaving no redos to re-stash
			UndoBranchStash<string> b = new UndoBranchStash<string>();
			b.Stash(new[] { "B" });
			b.LevelUndone();
			b.LevelCreated();
			Assert.Equal(0, b.Depth);
			Assert.False(b.IsValid(5, 0));
		}

		[Fact]
		public void DepthIsNotTrackedWithoutABranch()
		{
			UndoBranchStash<string> b = new UndoBranchStash<string>();
			b.LevelCreated(); b.LevelUndone(); b.LevelUndone(); b.LevelRedone();
			Assert.Equal(0, b.Depth);
			Assert.False(b.HasBranch);
			Assert.False(b.IsValid(10, 10));
		}

		[Fact]
		public void IsValidOnlyWhileTheBranchPointIsReachable()
		{
			UndoBranchStash<string> b = new UndoBranchStash<string>();
			b.Stash(new[] { "B" });
			b.LevelCreated(); b.LevelCreated(); b.LevelCreated();
			Assert.True(b.IsValid(3, 0));
			Assert.False(b.IsValid(2, 0), "level limit trimmed the branch point");

			b.LevelUndone(); b.LevelUndone(); b.LevelUndone(); b.LevelUndone();
			Assert.Equal(-1, b.Depth);
			Assert.True(b.IsValid(0, 1));
			Assert.False(b.IsValid(5, 0), "the common level below it is gone");

			Assert.Equal(new[] { "B" }, b.Invalidate().ToArray());
			Assert.False(b.IsValid(5, 5));
		}

		[Fact]
		public void SwapAwayFromTheBranchPointThrows()
		{
			UndoBranchStash<string> b = new UndoBranchStash<string>();
			b.Stash(new[] { "B" });
			b.LevelCreated();
			Assert.Throws<InvalidOperationException>(() => b.Swap(new[] { "C" }));
			Assert.Equal(new[] { "B" }, b.ToArrayForTest());
		}
	}

	internal static class UndoBranchStashTestExtensions
	{
		public static string[] ToArrayForTest(this UndoBranchStash<string> b)
		{
			string[] result = new string[b.Count];
			for(int i = 0; i < b.Count; i++) result[i] = b[i];
			return result;
		}
	}
}
