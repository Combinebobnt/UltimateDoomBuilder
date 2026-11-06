using CodeImp.DoomBuilder.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Builder.Tests
{
	public class PK3ReaderLoadFileTests : IDisposable
	{
		private readonly List<string> paths = new List<string>();

		public void Dispose()
		{
			foreach (string path in paths) File.Delete(path);
		}

		private string NewPath()
		{
			string path = Path.GetTempFileName();
			paths.Add(path);
			return path;
		}

		private static void WriteZip(string path, IEnumerable<KeyValuePair<string, string>> entries)
		{
			using (var stream = new FileStream(path, FileMode.Create))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				foreach (var entry in entries)
				{
					using (var writer = new StreamWriter(zip.CreateEntry(entry.Key, CompressionLevel.NoCompression).Open()))
						writer.Write(entry.Value);
				}
			}
		}

		private static KeyValuePair<string, string> Entry(string name, string content)
		{
			return new KeyValuePair<string, string>(name, content);
		}

		private static PK3Reader Open(string path)
		{
			// The real constructor needs a GameConfiguration, and LoadFile needs only the path from it
			var reader = (PK3Reader)RuntimeHelpers.GetUninitializedObject(typeof(PK3Reader));
			object location = new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, new List<string>());
			typeof(DataReader).GetField("location", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, location);
			reader.Silent = true;
			reader.BatchMode = true;
			return reader;
		}

		private static string Load(PK3Reader reader, string filename)
		{
			using (MemoryStream data = reader.LoadFile(filename))
				return data == null ? null : Encoding.UTF8.GetString(data.ToArray());
		}

		[Fact]
		public void CaseFoldsBackslashesAndKeepsTheFirstDuplicate()
		{
			string path = NewPath();
			WriteZip(path, new[]
			{
				Entry("dir/", ""),
				Entry("textures/Wall.png", "first"),
				Entry("TEXTURES/wall.png", "second"),
				Entry("back\\slash.png", "backslash"),
			});

			PK3Reader reader = Open(path);
			try
			{
				Assert.Equal("first", Load(reader, "textures/wall.png"));
				Assert.Equal("first", Load(reader, "TEXTURES/WALL.PNG"));
				Assert.Equal("backslash", Load(reader, "back/slash.png"));
				Assert.Null(Load(reader, "missing.png"));
				Assert.Null(Load(reader, "dir/"));
			}
			finally
			{
				reader.BatchMode = false;
			}
		}

		[Fact]
		public void ReopenedArchiveDoesNotUseEntriesOfTheOldOne()
		{
			// Entries of a closed archive read from a dead stream
			string path = NewPath();
			WriteZip(path, new[] { Entry("a.txt", "old") });

			PK3Reader reader = Open(path);
			try
			{
				Assert.Equal("old", Load(reader, "a.txt"));

				reader.BatchMode = false;
				WriteZip(path, new[] { Entry("b.txt", "added"), Entry("a.txt", "new") });
				reader.BatchMode = true;

				Assert.Equal("new", Load(reader, "a.txt"));
				Assert.Equal("added", Load(reader, "b.txt"));
			}
			finally
			{
				reader.BatchMode = false;
			}
		}

		[Fact]
		public void LookupCostDoesNotGrowWithTheEntryCount()
		{
			// A ratio on purpose: a scan makes the big archive hundreds of times slower, no scan about equal
			double small = BestLookupTime(10);
			double big = BestLookupTime(20000);

			Assert.True(big < small * 20, "small " + small + " ms, big " + big + " ms");
		}

		private double BestLookupTime(int entries)
		{
			string path = NewPath();
			WriteZip(path, Enumerable.Range(0, entries).Select(i => Entry("file" + i + ".txt", "x")));

			PK3Reader reader = Open(path);
			try
			{
				string last = "file" + (entries - 1) + ".txt";
				Assert.Equal("x", Load(reader, last));

				// Best of several, because other processes can only ever add to the time
				double best = double.MaxValue;
				for (int round = 0; round < 5; round++)
				{
					var timer = Stopwatch.StartNew();
					for (int i = 0; i < 20; i++) Load(reader, last);
					best = Math.Min(best, timer.Elapsed.TotalMilliseconds);
				}

				return best;
			}
			finally
			{
				reader.BatchMode = false;
			}
		}
	}
}
