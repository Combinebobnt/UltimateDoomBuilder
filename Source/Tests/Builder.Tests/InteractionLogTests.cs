using System;
using System.Collections.Generic;
using System.IO;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class InteractionLogTests
	{
		private sealed class Recorder
		{
			internal double Time;
			internal readonly List<Dictionary<string, string>> Lines = new List<Dictionary<string, string>>();
			internal readonly InteractionActionTracker Tracker;

			internal Recorder()
			{
				Tracker = new InteractionActionTracker(() => Time, Emit);
			}

			private void Emit(string ev, string[] fields)
			{
				Dictionary<string, string> d = new Dictionary<string, string> { { "ev", ev } };
				for(int i = 0; i + 1 < fields.Length; i += 2) d[fields[i]] = fields[i + 1];
				Lines.Add(d);
			}
		}

		private static readonly object ActionA = new object();
		private static readonly object ActionB = new object();

		[Fact]
		public void BeginThenEndPairsBySeq()
		{
			Recorder r = new Recorder();
			r.Tracker.SetTrigger(ActionA, "key", "Ctrl+Z");
			r.Tracker.BeginEnter(ActionA, "a");
			r.Time = 4;
			r.Tracker.BeginExit(ActionA);
			r.Time = 10;
			r.Tracker.EndEnter(ActionA, "a");
			r.Time = 11;
			r.Tracker.EndExit(ActionA);

			Assert.Equal(2, r.Lines.Count);
			Assert.Equal("action-begin", r.Lines[0]["ev"]);
			Assert.Equal("key", r.Lines[0]["trig"]);
			Assert.Equal("Ctrl+Z", r.Lines[0]["key"]);
			Assert.Equal("0", r.Lines[0]["depth"]);
			Assert.Equal("action-end", r.Lines[1]["ev"]);
			Assert.Equal(r.Lines[0]["seq"], r.Lines[1]["seq"]);
			Assert.Equal("4.0", r.Lines[1]["begin_ms"]);
			Assert.Equal("1.0", r.Lines[1]["end_ms"]);
			Assert.Equal("10.0", r.Lines[1]["hold_ms"]);
			Assert.False(r.Lines[1].ContainsKey("nested"));
		}

		[Fact]
		public void EndInsideBeginIsHeldUntilBeginReturns()
		{
			Recorder r = new Recorder();
			r.Tracker.BeginEnter(ActionA, "a");
			r.Time = 5;
			r.Tracker.EndEnter(ActionA, "a");
			r.Time = 6;
			r.Tracker.EndExit(ActionA);
			Assert.Equal(1, r.Lines.Count);
			r.Time = 30;
			r.Tracker.BeginExit(ActionA);

			Assert.Equal(2, r.Lines.Count);
			Assert.Equal("1", r.Lines[1]["nested"]);
			Assert.Equal("30.0", r.Lines[1]["begin_ms"]);
			Assert.Equal("1.0", r.Lines[1]["end_ms"]);
			Assert.Equal("5.0", r.Lines[1]["hold_ms"]);
		}

		[Fact]
		public void EndWithoutBeginHasNoSeq()
		{
			Recorder r = new Recorder();
			r.Tracker.SetTrigger(ActionA, "key", "Ctrl+P");
			Assert.False(r.Tracker.IsOpen(ActionA));
			r.Tracker.EndEnter(ActionA, "a");
			r.Time = 2;
			r.Tracker.EndExit(ActionA);

			Assert.Equal(1, r.Lines.Count);
			Assert.Equal("key", r.Lines[0]["trig"]);
			Assert.Equal("-", r.Lines[0]["seq"]);
			Assert.Equal("-", r.Lines[0]["begin_ms"]);
			Assert.Equal("2.0", r.Lines[0]["end_ms"]);
		}

		[Fact]
		public void RepeatedBeginThenOneEndClosesTheFirst()
		{
			Recorder r = new Recorder();
			r.Tracker.BeginEnter(ActionA, "a");
			r.Time = 1;
			r.Tracker.BeginExit(ActionA);
			r.Time = 100;
			r.Tracker.BeginEnter(ActionA, "a");
			r.Time = 103;
			r.Tracker.BeginExit(ActionA);
			r.Time = 200;
			r.Tracker.EndEnter(ActionA, "a");
			r.Tracker.EndExit(ActionA);

			Assert.Equal(4, r.Lines.Count);
			Assert.Equal("1", r.Lines[1]["rep"]);
			Assert.Equal("1", r.Lines[2]["rep"]);
			Assert.Equal(r.Lines[1]["seq"], r.Lines[2]["seq"]);
			Assert.Equal("3.0", r.Lines[2]["begin_ms"]);
			Assert.Equal(r.Lines[0]["seq"], r.Lines[3]["seq"]);
			Assert.Equal("200.0", r.Lines[3]["hold_ms"]);
		}

		[Fact]
		public void StaleOpenBeginIsReportedLost()
		{
			Recorder r = new Recorder();
			r.Tracker.BeginEnter(ActionA, "a");
			r.Tracker.BeginExit(ActionA);
			r.Time = InteractionActionTracker.STALE_MS + 1;
			r.Tracker.BeginEnter(ActionA, "a");

			Assert.Equal(3, r.Lines.Count);
			Assert.Equal("1", r.Lines[1]["lost"]);
			Assert.False(r.Lines[2].ContainsKey("rep"));
		}

		[Fact]
		public void OneKeyBeginningTwoActionsTagsBoth()
		{
			Recorder r = new Recorder();
			r.Tracker.SetTrigger(ActionA, "key", "Space");
			r.Tracker.BeginEnter(ActionA, "a");
			r.Tracker.BeginExit(ActionA);
			r.Tracker.SetTrigger(ActionB, "key", "Space");
			r.Tracker.BeginEnter(ActionB, "b");
			r.Tracker.BeginExit(ActionB);
			r.Tracker.BeginEnter(ActionA, "a2");

			Assert.Equal("key", r.Lines[0]["trig"]);
			Assert.Equal("key", r.Lines[1]["trig"]);
			Assert.Equal("code", r.Lines[2]["trig"]);
		}

		[Fact]
		public void AmbientTriggerAppliesToOutermostBeginOnly()
		{
			Recorder r = new Recorder();
			string previous = r.Tracker.SetAmbient("menu");
			r.Tracker.BeginEnter(ActionA, "a");
			r.Tracker.BeginEnter(ActionB, "b");
			r.Tracker.BeginExit(ActionB);
			r.Tracker.BeginExit(ActionA);
			r.Tracker.RestoreAmbient(previous);

			Assert.Equal("menu", r.Lines[0]["trig"]);
			Assert.Equal("code", r.Lines[1]["trig"]);
			Assert.Equal("1", r.Lines[1]["depth"]);
		}

		[Fact]
		public void DescriptionsKeepOnlyLeadingPlainWords()
		{
			string[,] cases =
			{
				{ "Change texture STARTAN2", "Change texture" },
				{ "Flood-fill textures with FLAT5_4", "Flood-fill textures with" },
				{ "Fit texture (BRICK1)", "Fit texture" },
				{ "Edit 12 linedefs", "Edit N linedefs" },
				{ "Toggle \"Not shown on automap\" linedef flag", "Toggle" },
				{ "STARTAN2 applied", "" },
				{ "Run script on many selected things", "Run script on many" },
			};
			List<string> failures = new List<string>();
			for(int i = 0; i < cases.GetLength(0); i++)
			{
				string got = InteractionLog.ReduceDescription(cases[i, 0]);
				if(got != cases[i, 1]) failures.Add(cases[i, 0] + " -> '" + got + "'");
			}
			Assert.Empty(failures);
		}

		[Fact]
		public void LineStartsWithTimeAndEventAndSanitisesValues()
		{
			string line = InteractionLog.FormatLine(12.34, "mark-note", new[] { "id", "1", "note", "a\tb\nc\rd" });
			Assert.Equal("t=12.3\tev=mark-note\tid=1\tnote=a b c d", line);
		}

		[Fact]
		public void FileLifecycleRotatesAndStopsCleanly()
		{
			string dir = Path.Combine(Path.GetTempPath(), "ilogtest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			long oldmax = InteractionLog.MaxBytes;
			try
			{
				string path = Path.Combine(dir, InteractionLog.FILE_NAME);
				File.WriteAllText(path, new string('x', 200));
				InteractionLog.MaxBytes = 100;

				Assert.True(InteractionLog.Open(path));
				Assert.True(File.Exists(path + ".1"));
				InteractionLog.Write("mark", "id", "1");
				InteractionLog.Stop();
				Assert.False(InteractionLog.Enabled);
				InteractionLog.Write("mark", "id", "2");

				string[] lines = File.ReadAllLines(path);
				Assert.Equal(2, lines.Length);
				Assert.Contains("\tev=mark\tid=1", lines[0]);
				Assert.Contains("\tev=end", lines[1]);

				File.Delete(path + ".1");
				Assert.True(InteractionLog.Open(path));
				for(int i = 0; i < 2000; i++) InteractionLog.Write("mark", "id", "x");
				InteractionLog.Stop();
				Assert.True(File.Exists(path + ".1"));
			}
			finally
			{
				InteractionLog.Stop();
				InteractionLog.MaxBytes = oldmax;
				Directory.Delete(dir, true);
			}
		}

		[Fact]
		public void RequestFollowsSettingAndEnvironment()
		{
			Assert.True(InteractionLog.IsRequested(true));
			if(Environment.GetEnvironmentVariable("UDB_INTERACTION_LOG") != "1") Assert.False(InteractionLog.IsRequested(false));
		}

		[Fact]
		public void UnopenableFileLeavesTheLogDisabled()
		{
			string path = Path.Combine(Path.GetTempPath(), "ilogtest-missing-" + Guid.NewGuid().ToString("N"), InteractionLog.FILE_NAME);
			Assert.False(InteractionLog.Open(path));
			Assert.False(InteractionLog.Enabled);
			InteractionLog.Write("mark", "id", "1");
			Assert.False(File.Exists(path));
		}
	}
}
