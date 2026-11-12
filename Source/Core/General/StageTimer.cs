using System.Diagnostics;
using System.Globalization;

namespace CodeImp.DoomBuilder
{
	internal sealed class StageTimer
	{
		private readonly string name;
		private readonly Stopwatch stopwatch = Stopwatch.StartNew();
		private double previous;

		internal StageTimer(string name)
		{
			this.name = name;
		}

		internal void Mark(string stage)
		{
			double elapsed = stopwatch.Elapsed.TotalMilliseconds;
			General.WriteLogLine(FormatLine(name, stage, elapsed - previous));
			previous = stopwatch.Elapsed.TotalMilliseconds;
		}

		internal void Done()
		{
			string total = FormatLine(name, "total", stopwatch.Elapsed.TotalMilliseconds);
			using(Process process = Process.GetCurrentProcess())
			{
				General.WriteLogLine(string.Format(CultureInfo.InvariantCulture,
					"{0}; peak working set {1:F3} MB; working set {2:F3} MB", total,
					process.PeakWorkingSet64 / 1048576.0, process.WorkingSet64 / 1048576.0));
			}
		}

		internal static string FormatLine(string name, string stage, double milliseconds)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0} timing: {1} {2:F3} ms", name, stage, milliseconds);
		}
	}
}
