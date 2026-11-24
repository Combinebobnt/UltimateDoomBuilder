#region ================== Namespaces

using System;
using System.Diagnostics;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// The map manager's lightmap preview: what the last check found, the versions it checked, and
	// the parsed bake it builds from. A change to the map hides the preview until the next check.
	internal sealed class LightmapPreviewState : IDisposable
	{
		#region ================== Constants

		private const int TICK_INTERVAL = 100;
		internal const long DEBOUNCE = 400; // ms the map must hold still before a re-check

		#endregion

		#region ================== Variables

		private long checkedchange = -1, checkedsource = -1;
		private long seenchange = -1, seensource = -1;
		private long seenat;
		private readonly Stopwatch clock = Stopwatch.StartNew();
		private bool valid;

		// The parsed bake, kept per bake so a check that only rematches the map doesn't read it again
		private string inputskey;
		private string inputserror;
		private LightmapData data;
		private GlSubsectors gl;
		private MapTopology baker;

		// The preview built from those inputs and the editor map it was matched against
		private LightmapPreview preview;
		private MapTopology matched;
		private string builderror;

		#endregion

		#region ================== Properties

		public Timer Timer { get; private set; }
		public string Reason { get; private set; }

		#endregion

		#region ================== Constructor / Disposer

		public LightmapPreviewState()
		{
			Reason = "the map was not checked yet";
			Timer = new Timer { Interval = TICK_INTERVAL };
			Timer.Start();
		}

		public void Dispose()
		{
			if(Timer == null) return;
			Timer.Stop();
			Timer.Dispose();
			Timer = null;
			preview = null;
			data = null;
		}

		#endregion

		#region ================== Versions

		internal static bool Same(long changeversion, long sourceversion, long checkedchange, long checkedsource)
		{
			return changeversion == checkedchange && sourceversion == checkedsource;
		}

		public bool Stale(long changeversion, long sourceversion)
		{
			return !Same(changeversion, sourceversion, checkedchange, checkedsource);
		}

		// Recorded when the check starts, so anything changed while it runs leaves it stale; nothing is
		// shown until its result is in
		public void MarkChecked(long changeversion, long sourceversion)
		{
			checkedchange = changeversion;
			checkedsource = sourceversion;
			valid = false;
		}

		public LightmapPreview Shown(long changeversion, long sourceversion, bool enabled)
		{
			if(!valid || preview == null || !enabled) return null;
			return Same(changeversion, sourceversion, checkedchange, checkedsource) ? preview : null;
		}

		public bool Settled(long changeversion, long sourceversion)
		{
			return Settled(changeversion, sourceversion, clock.ElapsedMilliseconds);
		}

		// True once a stale map has held still for the debounce; now is in ms on any steady clock
		internal bool Settled(long changeversion, long sourceversion, long now)
		{
			if(!Same(changeversion, sourceversion, seenchange, seensource))
			{
				seenchange = changeversion;
				seensource = sourceversion;
				seenat = now;
			}
			return Stale(changeversion, sourceversion) && now - seenat >= DEBOUNCE;
		}

		// Returns true when the reason differs from the last check's
		public bool SetResult(string reason)
		{
			bool changed = reason != Reason;
			valid = reason == null;
			Reason = reason;
			return changed;
		}

		// Why Shown returns null, or null when it doesn't
		public string Explain(long changeversion, long sourceversion, bool enabled)
		{
			if(!enabled) return "the lightmap preview is off";
			if(Shown(changeversion, sourceversion, enabled) != null) return null;
			if(Reason != null) return Reason;
			return "the map changed since the last check";
		}

		// Stands in for Build in tests
		internal void InjectForTest(LightmapPreview built)
		{
			preview = built;
		}

		#endregion

		#region ================== Building

		// Makes the preview for this bake and map current. Returns null when it can be drawn, else why not.
		public string Build(LightmapBakeCache bake, MapSet map)
		{
			string key = bake.WadPath + "|" + bake.Fingerprint;
			if(key != inputskey)
			{
				inputskey = key;
				preview = null;
				matched = null;
				inputserror = ReadInputs(bake.WadPath, out data, out gl, out baker);
			}
			if(inputserror != null)
			{
				// Not kept: a passing read failure (a locked file) is retried at the next check
				inputskey = null;
				return inputserror;
			}

			MapTopology current = MapTopology.FromMapSet(map);
			if(matched == null || !matched.SameAs(current))
			{
				matched = current;
				preview = LightmapPreview.Build(data, gl, baker, current, out builderror);
				if(preview != null) preview.Source = bake.Summary;
				else builderror = "the bake does not fit the map: " + builderror;
			}
			return preview != null ? null : builderror;
		}

		// Reads the bake's TEXTMAP, ZNODES and LIGHTMAP. Returns null, else why not.
		internal static string ReadInputs(string path, out LightmapData data, out GlSubsectors gl, out MapTopology baker)
		{
			data = null;
			gl = null;
			baker = null;
			string error;
			try
			{
				using(WAD wad = new WAD(path, true))
				{
					int header = wad.FindLumpIndex(MapManager.BUILD_MAP_HEADER);
					int end = header < 0 ? -1 : wad.FindLumpIndex("ENDMAP", header + 1);
					if(end < 0) return "the bake has no map";
					int textmap = wad.FindLumpIndex("TEXTMAP", header + 1, end);
					int nodes = wad.FindLumpIndex("ZNODES", header + 1, end);
					int lightmap = wad.FindLumpIndex(LightmapLump.LUMP_NAME, header + 1, end);
					if(textmap < 0 || nodes < 0 || lightmap < 0) return "the bake lacks its TEXTMAP, ZNODES or LIGHTMAP";

					baker = MapTopology.FromTextmap(BakedTextmap.ReadLines(wad.Lumps[textmap]), out error);
					if(baker == null) return "the bake's TEXTMAP: " + error;
					gl = GlSubsectors.Read(wad.Lumps[nodes].Stream.ReadAllBytes(), out error);
					if(gl == null) return "the bake's ZNODES: " + error;
					data = LightmapData.Read(wad.Lumps[lightmap].Stream.ReadAllBytes(), out error);
					if(data == null) return "the bake's LIGHTMAP: " + error;
					if(data.ProbeError != null) General.WriteLogLine("Lightmap preview: things are not lit from the bake, " + data.ProbeError);
				}
			}
			catch(Exception e)
			{
				return e.GetType().Name + " while reading the bake: " + e.Message;
			}
			return null;
		}

		#endregion
	}
}
