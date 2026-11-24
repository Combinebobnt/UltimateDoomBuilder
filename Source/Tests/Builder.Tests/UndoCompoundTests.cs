using CodeImp.DoomBuilder.Editing;
using Xunit;

namespace Builder.Tests
{
	// A wrong withdraw verdict either rolls back a run that should go on or keeps a cancelled one
	public class UndoCompoundTests
	{
		private static UndoCompound Started()
		{
			UndoCompound c = new UndoCompound();
			c.Begin(7);
			return c;
		}

		[Fact]
		public void WithdrawDecisionTable()
		{
			// (mark set by the last inner create, commands now) -> abort?
			int[][] cases = { new[] { -1, 0, 0 }, new[] { -1, 3, 1 }, new[] { 0, 0, 0 }, new[] { 4, 4, 0 }, new[] { 4, 5, 1 }, new[] { 0, 2, 1 } };
			foreach(int[] row in cases)
			{
				UndoCompound c = Started();
				if(row[0] >= 0) c.MarkCreate(row[0]);
				UndoCompoundWithdraw w = c.OnWithdraw(row[1], "cancel");
				Assert.True((w == UndoCompoundWithdraw.Abort) == (row[2] == 1), "mark " + row[0] + ", commands " + row[1]);
				Assert.True(c.Aborted == (row[2] == 1), "aborted state for mark " + row[0] + ", commands " + row[1]);
			}
		}

		[Fact]
		public void NoOpWithdrawKeepsTheRunGoing()
		{
			UndoCompound c = Started();
			c.MarkCreate(3);
			Assert.Equal(UndoCompoundWithdraw.NoOp, c.OnWithdraw(3, "x"));
			c.MarkCreate(3);
			Assert.False(c.Aborted);
			Assert.True(c.Active);
			Assert.True(c.HasLevel);
			Assert.Equal(7, c.Ticket);
		}

		[Fact]
		public void FirstAbortReasonWinsAndAbortIsIdempotent()
		{
			UndoCompound c = Started();
			c.Abort("first");
			c.Abort("second");
			Assert.Equal(UndoCompoundWithdraw.Abort, c.OnWithdraw(9, "third"));
			Assert.True(c.Aborted);
			Assert.Equal("first", c.AbortReason);
		}

		[Fact]
		public void DropAbortsAndMarksDropped()
		{
			UndoCompound c = Started();
			c.Drop();
			Assert.True(c.Dropped);
			Assert.True(c.Aborted);
		}

		[Fact]
		public void EndResetsAndBeginRules()
		{
			UndoCompound c = Started();
			Assert.Throws<System.InvalidOperationException>(() => c.Begin(8));
			c.Abort("x");
			c.End();
			Assert.False(c.Active);
			Assert.False(c.HasLevel);
			Assert.Equal(UndoCompoundWithdraw.NoOp, c.OnWithdraw(5, "y"));
			c.Begin(9);
			Assert.False(c.Aborted);
			Assert.Null(c.AbortReason);
			Assert.Throws<System.ArgumentException>(() => new UndoCompound().Begin(-1));
		}
	}
}
