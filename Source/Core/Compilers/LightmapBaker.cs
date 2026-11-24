#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

#endregion

namespace CodeImp.DoomBuilder.Compilers
{
	internal sealed class LightmapBakeSettings
	{
		public const int DEFAULT_TEXTURE_SIZE = 1024;
		public static readonly int[] TEXTURE_SIZES = { 128, 256, 512, 1024, 2048, 4096 };

		public string BakerPath = string.Empty;
		public int TextureSize = DEFAULT_TEXTURE_SIZE;
		public int Threads; // 0 = automatic
		public bool CpuOnly;

		// The baker's own default is 64 threads, too many for most machines
		public int EffectiveThreads(int processorcount)
		{
			return (Threads > 0 ? Threads : Math.Max(1, Math.Min(processorcount, 4)));
		}

		// Null when the baker can run, else why not
		public string BakerProblem()
		{
			if(string.IsNullOrEmpty(BakerPath)) return "No ZDRay executable is set.";
			if(!File.Exists(BakerPath)) return "The ZDRay executable was not found: " + BakerPath;
			return null;
		}
	}

	// A successful bake's output WAD and the fingerprint of the map it was baked from
	internal sealed class LightmapBakeCache
	{
		public readonly string WadPath;
		public readonly string Fingerprint;
		public readonly string Summary;

		public LightmapBakeCache(string wadpath, string fingerprint, string summary)
		{
			WadPath = wadpath;
			Fingerprint = fingerprint;
			Summary = summary;
		}
	}

	// One bake run: its input and output files, deleted on Dispose unless the output was taken
	internal sealed class LightmapBakeJob : IDisposable
	{
		public readonly string InputPath;
		public readonly string OutputPath;
		public readonly string Fingerprint;
		public readonly LightmapBaker Baker = new LightmapBaker();
		private bool outputtaken;

		public LightmapBakeJob(string inputpath, string outputpath, string fingerprint)
		{
			InputPath = inputpath;
			OutputPath = outputpath;
			Fingerprint = fingerprint;
		}

		public string TakeOutput()
		{
			outputtaken = true;
			return OutputPath;
		}

		public void Dispose()
		{
			Baker.Dispose();
			Delete(InputPath);
			if(!outputtaken) Delete(OutputPath);
		}

		private static void Delete(string path)
		{
			try { if(File.Exists(path)) File.Delete(path); }
			catch(IOException) { }
			catch(UnauthorizedAccessException) { }
		}
	}

	// Parses the baker's console output. Progress segments end in '\r', other lines in '\n'.
	internal sealed class LightmapBakeProgress
	{
		private const int MAX_LINES = 8;

		private static readonly Regex SIDES = new Regex(@"^Sides:\s*(\d+)\s*/\s*(\d+)$");
		private static readonly Regex SUBSECTORS = new Regex(@"^Subsectors:\s*(\d+)\s*/\s*(\d+)$");
		private static readonly Regex TRACING = new Regex(@"^(\d+(?:\.\d+)?)%\s+(\d+)\s*/\s*(\d+)$");
		private static readonly Regex BSP = new Regex(@"^BSP:\s*(\d+(?:\.\d+)?)%$");

		private readonly Queue<string> lines = new Queue<string>();
		private readonly Dictionary<bool, string> partial = new Dictionary<bool, string> { { false, "" }, { true, "" } };

		public string Stage { get; private set; }
		public double Fraction { get; private set; } // -1 when unknown

		public LightmapBakeProgress()
		{
			Stage = "Starting";
			Fraction = -1;
		}

		public string[] LastLines { get { return lines.ToArray(); } }

		// Feeds a chunk of one stream; text may split anywhere
		public void Feed(string text, bool stderr)
		{
			string pending = partial[stderr] + text;
			int start = 0;
			for(int i = 0; i < pending.Length; i++)
			{
				if(pending[i] != '\r' && pending[i] != '\n') continue;
				Segment(pending.Substring(start, i - start));
				start = i + 1;
			}
			partial[stderr] = pending.Substring(start);
		}

		// Flushes an unterminated last segment when the process has ended
		public void End()
		{
			foreach(bool stream in new[] { false, true })
			{
				if(partial[stream].Length > 0) Segment(partial[stream]);
				partial[stream] = "";
			}
		}

		private void Segment(string raw)
		{
			string s = raw.Trim();
			if(s.Length == 0) return;

			Match m;
			if((m = SIDES.Match(s)).Success) SetFraction("Building side surfaces", m.Groups[1].Value, m.Groups[2].Value);
			else if((m = SUBSECTORS.Match(s)).Success) SetFraction("Building subsector surfaces", m.Groups[1].Value, m.Groups[2].Value);
			else if((m = TRACING.Match(s)).Success) SetFraction("Ray tracing", m.Groups[2].Value, m.Groups[3].Value);
			else if((m = BSP.Match(s)).Success)
			{
				Stage = "Building nodes";
				Fraction = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0;
			}
			else
			{
				if(s == "Ray tracing in progress...") { Stage = "Ray tracing"; Fraction = 0; }
				else if(s == "Ray tracing complete") { Stage = "Writing output"; Fraction = -1; }
				lines.Enqueue(s);
				while(lines.Count > MAX_LINES) lines.Dequeue();
			}
		}

		private void SetFraction(string stage, string done, string total)
		{
			Stage = stage;
			long d = long.Parse(done, CultureInfo.InvariantCulture);
			long t = long.Parse(total, CultureInfo.InvariantCulture);
			Fraction = (t > 0 ? Math.Min(1.0, (double)d / t) : -1);
		}
	}

	// Runs the ZDRay baker on a prepared WAD, off the UI thread, and can be cancelled
	internal sealed class LightmapBaker : IDisposable
	{
		private readonly object sync = new object();
		private readonly LightmapBakeProgress progress = new LightmapBakeProgress();
		private readonly Stopwatch timer = new Stopwatch();
		private Process process;
		private Thread[] readers;
		private volatile bool cancelled;
		private int exitcode = -1;
		private bool finished;

		public bool Cancelled { get { return cancelled; } }
		public TimeSpan Elapsed { get { return timer.Elapsed; } }

		// Arguments in zdbsp style: GL nodes in XGLN form, as UDB's zdbsp profiles write them
		public static string BuildArguments(LightmapBakeSettings settings, int processorcount, string mapname, string inputfile, string outputfile)
		{
			List<string> args = new List<string> { "-c", "-X", "-m", mapname };
			if(settings.CpuOnly) args.Add("-C");
			args.Add("-j");
			args.Add(settings.EffectiveThreads(processorcount).ToString(CultureInfo.InvariantCulture));
			args.Add("-S");
			args.Add(settings.TextureSize.ToString(CultureInfo.InvariantCulture));
			args.Add("-o");
			args.Add(Quote(outputfile));
			args.Add(Quote(inputfile));
			return string.Join(" ", args);
		}

		private static string Quote(string path) { return "\"" + path + "\""; }

		public void Start(LightmapBakeSettings settings, string inputfile, string outputfile)
		{
			ProcessStartInfo info = new ProcessStartInfo
			{
				FileName = settings.BakerPath,
				Arguments = BuildArguments(settings, Environment.ProcessorCount, MapManager.BUILD_MAP_HEADER, inputfile, outputfile),
				WorkingDirectory = Path.GetDirectoryName(inputfile),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			General.WriteLogLine("Running lightmap baker: " + info.FileName + " " + info.Arguments);
			timer.Start();
			process = Process.Start(info);
			readers = new[] { Reader(process.StandardOutput, false), Reader(process.StandardError, true) };
			foreach(Thread t in readers) t.Start();
		}

		private Thread Reader(StreamReader stream, bool stderr)
		{
			Thread t = new Thread(() =>
			{
				char[] buffer = new char[4096];
				int count;
				try
				{
					while((count = stream.Read(buffer, 0, buffer.Length)) > 0)
						lock(sync) progress.Feed(new string(buffer, 0, count), stderr);
				}
				catch(IOException) { }
				catch(ObjectDisposedException) { }
			});
			t.IsBackground = true;
			t.Name = "Lightmap baker " + (stderr ? "stderr" : "stdout");
			return t;
		}

		// True once the process has exited and its output is drained
		public bool Poll(out int code)
		{
			lock(sync)
			{
				if(!finished && process != null && process.HasExited && Array.TrueForAll(readers, t => !t.IsAlive))
				{
					finished = true;
					exitcode = process.ExitCode;
					progress.End();
					timer.Stop();
				}
				code = exitcode;
				return finished;
			}
		}

		public void Snapshot(out string stage, out double fraction, out string[] lastlines)
		{
			lock(sync)
			{
				stage = progress.Stage;
				fraction = progress.Fraction;
				lastlines = progress.LastLines;
			}
		}

		public void Cancel()
		{
			cancelled = true;
			try { if(process != null && !process.HasExited) process.Kill(); }
			catch(InvalidOperationException) { }
			catch(System.ComponentModel.Win32Exception) { }
		}

		// Blocks until the process and its readers are gone
		public void WaitForExit()
		{
			if(process == null) return;
			process.WaitForExit();
			foreach(Thread t in readers) t.Join();
			int code;
			Poll(out code);
		}

		public void Dispose()
		{
			if(process == null) return;
			if(!process.HasExited) Cancel();
			WaitForExit();
			process.Dispose();
			process = null;
		}
	}
}
