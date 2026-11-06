
#region ================== Namespaces

using System;
using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Editing
{
	// Bookkeeping for the one redo branch an edit after undoing would otherwise discard. Never touches the map.
	internal sealed class UndoBranchStash<T>
	{
		#region ================== Variables

		// Same order as the redo list: index 0 is the next level to reapply from the branch point
		private readonly List<T> levels = new List<T>();

		// Signed distance from the branch point to the current state along the current line:
		// +k means k levels above it are created or redone, -k means k common levels below it are undone
		private int depth;

		// A level created below the branch point forks the line under it, so the branch point is gone
		private bool forkedbelow;

		#endregion

		#region ================== Properties

		public int Count { get { return levels.Count; } }
		public T this[int index] { get { return levels[index]; } }
		public int Depth { get { return depth; } }
		public bool HasBranch { get { return levels.Count > 0; } }

		#endregion

		#region ================== Methods

		// Holds the given levels as the branch, branching at the current state. Returns the replaced levels.
		public List<T> Stash(IEnumerable<T> discarded)
		{
			List<T> replaced = new List<T>(levels);
			levels.Clear();
			levels.AddRange(discarded);
			depth = 0;
			forkedbelow = false;
			return replaced;
		}

		// UndoManager re-stashes before an edit at negative depth, so getting here below the
		// branch point means its common redo levels were disposed (a withdrawn undo)
		public void LevelCreated()
		{
			if(levels.Count == 0) return;
			if(depth < 0) forkedbelow = true;
			depth++;
		}

		public void LevelUndone() { if(levels.Count > 0) depth--; }
		public void LevelRedone() { if(levels.Count > 0) depth++; }

		// True when a new discard of this size should be disposed instead of replacing the branch:
		// the branch point is at or below the current state, so the branch stays reachable, and it is not smaller
		public bool KeepsOver(int discardedcount)
		{
			return (levels.Count > 0) && !forkedbelow && (depth >= 0) && (levels.Count >= discardedcount);
		}

		// Drops the branch and returns its levels
		public List<T> Invalidate()
		{
			return Stash(new T[0]);
		}

		// The branch point is reachable while the current line still has the levels to walk back to it
		public bool IsValid(int undocount, int redocount)
		{
			if((levels.Count == 0) || forkedbelow) return false;
			return (depth >= 0) ? (depth <= undocount) : (-depth <= redocount);
		}

		// At the branch point only: holds the current redo levels as the branch and returns the stashed ones
		public List<T> Swap(IEnumerable<T> currentredos)
		{
			if(depth != 0) throw new InvalidOperationException("Undo branch swap away from the branch point (depth " + depth + ")");
			return Stash(currentredos);
		}

		#endregion
	}
}
