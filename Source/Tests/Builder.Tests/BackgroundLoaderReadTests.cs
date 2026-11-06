using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	// What the Background Loader threads hit inside a reader: StopBackgroundLoader's interrupt and a pk3 gone bad
	public class BackgroundLoaderReadTests : IDisposable
	{
		private const BindingFlags INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
		private readonly List<string> paths = new List<string>();

		public void Dispose()
		{
			foreach (string path in paths)
			{
				if (Directory.Exists(path)) Directory.Delete(path, true);
				else if (File.Exists(path)) File.Delete(path);
			}
		}

		private string NewPath()
		{
			string path = Path.GetTempFileName();
			paths.Add(path);
			return path;
		}

		private string NewDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "udbtest-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			paths.Add(path);
			return path;
		}

		private static void SetField(Type type, object target, string field, object value)
		{
			type.GetField(field, INSTANCE)!.SetValue(target, value);
		}

		private static DirectoryReader OpenDirectory(string dir)
		{
			// The real constructor needs a GameConfiguration and General.Map
			var reader = (DirectoryReader)RuntimeHelpers.GetUninitializedObject(typeof(DirectoryReader));
			SetField(typeof(DataReader), reader, "location", new DataLocation(DataLocation.RESOURCE_DIRECTORY, dir, false, false, false, new List<string>()));
			SetField(typeof(DirectoryReader), reader, "files", new DirectoryFilesList(dir, null, true, FileTitleStyle.DEFAULT));
			SetField(typeof(PK3StructuredReader), reader, "wads", new List<WADReader>());
			reader.Silent = true;
			return reader;
		}

		// Left out of batch mode, so every LoadFile reopens the archive like the Background Loader's loads do
		private static PK3Reader OpenPK3(string path, params string[] names)
		{
			var reader = (PK3Reader)RuntimeHelpers.GetUninitializedObject(typeof(PK3Reader));
			SetField(typeof(DataReader), reader, "location", new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, new List<string>()));
			var entries = names.Select(n => new DirectoryFileEntry(n, FileTitleStyle.DEFAULT)).ToList();
			SetField(typeof(PK3Reader), reader, "files", new DirectoryFilesList(path, null, true, entries));
			SetField(typeof(PK3StructuredReader), reader, "wads", new List<WADReader>());
			reader.Silent = true;
			return reader;
		}

		private static void WriteZip(string path, params string[] names)
		{
			using (var stream = new FileStream(path, FileMode.Create))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				foreach (string name in names)
				{
					using (var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open()))
						writer.Write("data of " + name);
				}
			}
		}

		private static PK3FileImage NewImage(PK3Reader reader, string filepathname)
		{
			// The real constructor reads scales from General.Map.Config
			var image = (PK3FileImage)RuntimeHelpers.GetUninitializedObject(typeof(PK3FileImage));
			SetField(typeof(PK3FileImage), image, "datareader", reader);
			SetField(typeof(ImageData), image, "filepathname", filepathname);
			SetField(typeof(ImageData), image, "name", Path.GetFileNameWithoutExtension(filepathname).ToUpperInvariant());
			return image;
		}

		private static object LocalLoadImage(PK3FileImage image)
		{
			try
			{
				return typeof(PK3FileImage).GetMethod("LocalLoadImage", INSTANCE)!.Invoke(image, null);
			}
			catch (TargetInvocationException e)
			{
				throw e.InnerException!;
			}
		}

		// Runs call on a thread that blocks on lockobj's lock, interrupts it there, and returns what it threw
		private static Exception InterruptOnLock(object lockobj, Func<object> call)
		{
			Exception thrown = null;
			var worker = new Thread(() =>
			{
				try { call(); }
				catch (Exception e) { thrown = e; }
			});

			lock (lockobj)
			{
				worker.Start();
				var timer = Stopwatch.StartNew();
				while ((worker.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0 && timer.ElapsedMilliseconds < 5000)
					Thread.Sleep(1);
				worker.Interrupt();
			}

			Assert.True(worker.Join(5000), "worker still running");
			return thrown;
		}

		[Fact]
		public void InterruptInsideAReaderReachesTheLoaderThread()
		{
			string dir = NewDirectory();
			foreach (string file in new[] { "patches/P1.png", "textures/T1.png", "hires/H1.png", "colormaps/C1.lmp", "sprites/S1A0.png", "voxels/V1.kvx", "plain.txt" })
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file))!);
				File.WriteAllText(Path.Combine(dir, file), "data of " + file);
			}
			DirectoryReader directory = OpenDirectory(dir);

			string pk3path = NewPath();
			WriteZip(pk3path, "voxels/V2.kvx", "textures/T2.png");
			PK3Reader pk3 = OpenPK3(pk3path, "voxels/V2.kvx", "textures/T2.png");

			string location = null;
			var calls = new List<Tuple<string, object, Func<object>>>
			{
				Tuple.Create<string, object, Func<object>>("DirectoryReader.LoadFile", directory, () => directory.LoadFile("plain.txt")),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetPatchData", directory, () => directory.GetPatchData("patches/P1.png", true, ref location)),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetTextureData", directory, () => directory.GetTextureData("textures/T1.png", true, ref location)),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetHiResTextureData", directory, () => directory.GetHiResTextureData("H1", ref location)),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetColormapData", directory, () => directory.GetColormapData("C1")),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetSpriteData", directory, () => directory.GetSpriteData("S1A0", ref location)),
				Tuple.Create<string, object, Func<object>>("DirectoryReader.GetVoxelData", directory, () => directory.GetVoxelData("V1", ref location)),
				Tuple.Create<string, object, Func<object>>("PK3Reader.GetVoxelData", pk3, () => pk3.GetVoxelData("V2", ref location)),
			};

			var failures = new List<string>();
			foreach (var call in calls)
			{
				// Each lookup must reach the file, or the interrupt below proves nothing
				object found = call.Item3();
				if (found == null) { failures.Add(call.Item1 + ": found nothing"); continue; }
				((IDisposable)found).Dispose();

				Exception thrown = InterruptOnLock(call.Item2, call.Item3);
				if (!(thrown is ThreadInterruptedException))
					failures.Add(call.Item1 + ": " + (thrown == null ? "returned normally" : thrown.GetType().Name));
			}

			// The image wrapper must pass it on too, so its read-failure catch can't swallow it
			PK3FileImage image = NewImage(pk3, "textures/T2.png");
			Exception imagethrown = InterruptOnLock(pk3, () => LocalLoadImage(image));
			if (!(imagethrown is ThreadInterruptedException))
				failures.Add("PK3FileImage.LocalLoadImage: " + (imagethrown == null ? "returned normally" : imagethrown.GetType().Name));

			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}

		[Theory]
		[InlineData("deleted")]
		[InlineData("truncated")]
		public void DamagedPK3MarksTheImageFailed(string damage)
		{
			string path = NewPath();
			WriteZip(path, "textures/T1.png");
			PK3Reader reader = OpenPK3(path, "textures/T1.png");
			PK3FileImage image = NewImage(reader, "textures/T1.png");

			if (damage == "deleted") File.Delete(path);
			else File.WriteAllBytes(path, File.ReadAllBytes(path).Take(10).ToArray());

			object result = LocalLoadImage(image);

			Type resulttype = result.GetType();
			Assert.Null(resulttype.GetField("bitmap")!.GetValue(result));
			var messages = ((IEnumerable)resulttype.GetField("messages")!.GetValue(result)!).Cast<object>().ToList();
			Assert.True(messages.Any(m => (ErrorType)m.GetType().GetProperty("Type")!.GetValue(m)! == ErrorType.Error), "no error message");

			// A failed reopen must not keep the stream it opened
			Assert.Null(typeof(PK3Reader).GetField("filestream", INSTANCE)!.GetValue(reader));
		}
	}
}
