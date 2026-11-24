
#region ================== Namespaces

using System;

#endregion

namespace CodeImp.DoomBuilder.Editing
{
	internal delegate void UndoCreatedHandler(string description);

	internal enum UndoCompoundWithdraw
	{
		NoOp,
		Abort,
	}

	// State of a run that gathers every undo it creates into one level. Never touches the map.
	internal sealed class UndoCompound
	{
		#region ================== Variables

		private bool active;
		private int ticket;
		private int mark;
		private bool aborted;
		private string abortreason;
		private bool dropped;

		#endregion

		#region ================== Properties

		public bool Active { get { return active; } }
		public bool HasLevel { get { return active && (ticket > 0); } }
		public int Ticket { get { return ticket; } }
		public int Mark { get { return mark; } }
		public bool Aborted { get { return aborted; } }
		public string AbortReason { get { return abortreason; } }

		// The history was cleared under the run (a map switch): nothing is left to keep or roll back
		public bool Dropped { get { return dropped; } }

		#endregion

		#region ================== Methods

		public void Begin(int levelticket)
		{
			if(active) throw new InvalidOperationException("An undo compound is already active");
			if(levelticket <= 0) throw new ArgumentException("An undo compound needs a real ticket");
			active = true;
			ticket = levelticket;
			mark = 0;
			aborted = false;
			abortreason = null;
			dropped = false;
		}

		// An inner CreateUndo: a later withdraw compares against this command count
		public void MarkCreate(int commandcount)
		{
			if(active) mark = commandcount;
		}

		// A withdraw with nothing recorded since the last inner create undid nothing
		public UndoCompoundWithdraw OnWithdraw(int commandcount, string reason)
		{
			if(!active || (commandcount <= mark)) return UndoCompoundWithdraw.NoOp;
			Abort(reason);
			return UndoCompoundWithdraw.Abort;
		}

		// The first reason wins
		public void Abort(string reason)
		{
			if(!active || aborted) return;
			aborted = true;
			abortreason = reason;
		}

		public void Drop()
		{
			if(!active) return;
			dropped = true;
			Abort("the undo history was cleared");
		}

		public void End()
		{
			active = false;
			ticket = 0;
			mark = 0;
		}

		#endregion
	}
}
