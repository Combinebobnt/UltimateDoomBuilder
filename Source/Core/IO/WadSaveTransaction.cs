#region ================== Namespaces

using System;
using System.IO;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	internal sealed class WadSaveTransaction : IDisposable
	{
		#region ================== Constants

		internal const string SIBLING_SUFFIX = SiblingFileSwap.SIBLING_SUFFIX;

		#endregion

		#region ================== Variables

		private readonly SiblingFileSwap swap;
		private bool isdisposed;

		#endregion

		#region ================== Properties

		internal WAD Wad { get; private set; }
		internal string MetadataWarning { get { return swap.MetadataWarning; } }
		internal string SiblingPath { get { return swap.SiblingPath; } }

		#endregion

		#region ================== Constructor / Disposer

		internal WadSaveTransaction(string targetpath, string basepath)
		{
			// A leftover sibling is a save that never took effect. WAD would parse it, so the swap deletes it first
			swap = new SiblingFileSwap(targetpath);

			try
			{
				Wad = new WAD(swap.SiblingPath);

				// Rebuild from the base, since WADs whose directory is not at the end break otherwise (#531)
				if(!string.IsNullOrEmpty(basepath))
				{
					WAD basewad = new WAD(basepath, true);
					try
					{
						Wad.IsIWAD = basewad.IsIWAD;
						CopyAllLumps(basewad, Wad);
					}
					finally
					{
						basewad.Dispose();
					}
				}
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if(isdisposed) return;
			isdisposed = true;

			if(Wad != null)
			{
				// A failed flush has already been reported by the call that hit it
				try { Wad.Dispose(); }
				catch(Exception) { }
				Wad = null;
			}

			swap.Dispose();
		}

		#endregion

		#region ================== Methods

		internal void Seal()
		{
			if(Wad == null) throw new InvalidOperationException("The save transaction is already sealed.");

			Wad.FlushToDisk();
			Wad.Dispose();
			Wad = null;

			swap.CopyMetadataFromTarget();
		}

		internal void Swap()
		{
			if(Wad != null || isdisposed) throw new InvalidOperationException("The save transaction must be sealed and open to swap.");
			swap.Swap();
		}

		private static void CopyAllLumps(WAD source, WAD target)
		{
			for(int i = 0; i < source.Lumps.Count; i++)
			{
				Lump srclump = source.Lumps[i];
				Lump tgtlump = target.Insert(srclump.Name, target.Lumps.Count, srclump.Length, false);
				srclump.CopyTo(tgtlump);
			}

			target.WriteHeaders();
		}

		#endregion
	}
}
