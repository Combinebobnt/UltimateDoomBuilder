using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Data.Scripting;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class ResourceImageRegistrationTests : IDisposable
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
		private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
		private readonly SndInfoParserTests.FakeMap map = new SndInfoParserTests.FakeMap("doom");
		private readonly string directory = Path.Combine(Path.GetTempPath(), "image-registration-" + Guid.NewGuid().ToString("N"));
		private readonly object previouswindow = typeof(General).GetField("mainwindow", Static)!.GetValue(null);
		private readonly object previoustemp = typeof(General).GetField("temppath", Static)!.GetValue(null);
		private readonly object previousassembly = typeof(General).GetField("thisasm", Static)!.GetValue(null);
		private readonly object previousscripts = typeof(General).GetField("scriptconfigs", Static)!.GetValue(null);
		private readonly Bitmap bitmap = new Bitmap(1, 1);
		private readonly Graphics graphics;
		private readonly List<DataReader> readers = new List<DataReader>();
		private readonly List<DataManager> managers = new List<DataManager>();

		public ResourceImageRegistrationTests()
		{
			Directory.CreateDirectory(directory);
			graphics = Graphics.FromImage(bitmap);
			// Image constructors only need the real text-measurement method, not a window or GL device.
			var window = (MainForm)RuntimeHelpers.GetUninitializedObject(typeof(MainForm));
			GC.SuppressFinalize(window);
			Set(window, "graphics", graphics);
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, window);
			typeof(General).GetField("temppath", Static)!.SetValue(null, directory);
			typeof(General).GetField("thisasm", Static)!.SetValue(null, typeof(General).Assembly);
			typeof(General).GetField("scriptconfigs", Static)!.SetValue(null, new Dictionary<string, ScriptConfiguration>());
			Set(General.Map, "temppath", directory);
			try { Configure("gzdoom"); }
			catch { Dispose(); throw; }
		}

		public void Dispose()
		{
			foreach(DataManager data in managers)
			{
				var images = Get<Dictionary<int, ImageData>>(data, "animationimages").Values
					.Concat(Get<Dictionary<string, ImageData>>(data, "fullmaterialimages").Values)
					.Concat(Get<Dictionary<ImageData, ImageData>>(data, "shortmaterialimages").Keys)
					.Concat(Get<Dictionary<ImageData, ImageData>>(data, "shortmaterialimages").Values).Distinct();
				foreach(ImageData image in images) if(image != null) image.Dispose();
			}
			foreach(DataReader reader in readers) reader.Dispose();
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, previouswindow);
			typeof(General).GetField("temppath", Static)!.SetValue(null, previoustemp);
			typeof(General).GetField("thisasm", Static)!.SetValue(null, previousassembly);
			typeof(General).GetField("scriptconfigs", Static)!.SetValue(null, previousscripts);
			graphics.Dispose(); bitmap.Dispose(); map.Dispose();
			Directory.Delete(directory, true);
		}

		private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
		private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target);
		private static object Call(DataManager target, string name, params object[] args) => typeof(DataManager).GetMethod(name, Instance)!.Invoke(target, args);

		private void Configure(string engine)
		{
			var config = new Configuration();
			Assert.True(config.InputConfiguration("basegame = \"doom\"; engine = \"" + engine + "\"; longtexturenames = true; formatinterface = \"DoomMapSetIO\";"));
			Set(General.Map, "config", new GameConfiguration(config));
		}

		private DataManager Load(params DataReader[] resources)
		{
			var data = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
			Set(data, "containers", resources.ToList());
			Set(data, "animator", new TextureAnimator()); Set(data, "animationorder", new TextureAnimationOrder());
			Set(data, "animationimages", new Dictionary<int, ImageData>()); Set(data, "animationkeys", new Dictionary<ImageData, int>());
			Set(data, "materialregistrations", Activator.CreateInstance(typeof(DataManager).GetField("materialregistrations", Instance)!.FieldType));
			Set(data, "warps", new Dictionary<ImageData, WarpInfo>());
			Set(data, "shortmaterialimages", new Dictionary<ImageData, ImageData>());
			Set(data, "fullmaterialimages", new Dictionary<string, ImageData>(StringComparer.OrdinalIgnoreCase));
			Set(data, "textures", new Dictionary<long, ImageData>()); Set(data, "flats", new Dictionary<long, ImageData>());
			Set(data, "texturenamesshorttofull", new Dictionary<long, long>()); Set(data, "flatnamesshorttofull", new Dictionary<long, long>());
			Set(data, "scriptresources", new Dictionary<ScriptType, HashSet<ScriptResource>>());
			Set(General.Map, "data", data);
			var parsers = new Dictionary<string, TexturesParser>();
			Call(data, "LoadTextures", Get<Dictionary<long, ImageData>>(data, "textures"), Get<Dictionary<long, long>>(data, "texturenamesshorttofull"), parsers);
			Call(data, "LoadFlats", Get<Dictionary<long, ImageData>>(data, "flats"), Get<Dictionary<long, long>>(data, "flatnamesshorttofull"), parsers);
			Get<TextureAnimationOrder>(data, "animationorder").Seal();
			Call(data, "SplitMaterialImageIdentities");
			managers.Add(data);
			return data;
		}

		private PK3Reader Archive(params KeyValuePair<string, byte[]>[] entries)
		{
			string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pk3");
			using(var stream = File.Create(path))
			using(var zip = new ZipArchive(stream, ZipArchiveMode.Create))
				foreach(var entry in entries) using(var output = zip.CreateEntry(entry.Key).Open()) output.Write(entry.Value, 0, entry.Value.Length);
			var reader = new PK3Reader(new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, null), General.Map.Config, true);
			readers.Add(reader);
			return reader;
		}

		private static KeyValuePair<string, byte[]> Text(string path, string text) => new KeyValuePair<string, byte[]>(path, Encoding.ASCII.GetBytes(text));
		private static KeyValuePair<string, byte[]> Image(string path)
		{
			using(var image = new Bitmap(8, 8))
			using(var stream = new MemoryStream())
			{
				image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
				return new KeyValuePair<string, byte[]>(path, stream.ToArray());
			}
		}

		private string Wad(string text)
		{
			string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wad");
			using(var wad = new WAD(path))
			{
				byte[] bytes = Encoding.ASCII.GetBytes(text);
				wad.Insert("TEXTURES", 0, bytes.Length).Stream.Write(bytes, 0, bytes.Length);
			}
			return path;
		}

		private static ImageData Target(DataManager data, string name, MaterialTarget target, bool overridable)
		{
			return (ImageData)Call(data, "FindMaterialTarget", new GldefsMaterial { Name = name, Target = target, Overridable = overridable });
		}

		private static void Animate(DataManager data, AnimdefsParser parser)
		{
			// Pre-create file images while text measurement is available; diagnostics need no window.
			foreach(string name in parser.Animations.SelectMany(animation => new[] { animation.Name }.Concat(animation.Frames.Select(frame => frame.Name))))
				if(name != null && name.Contains("/")) data.GetTextureImage(name);
			object window = typeof(General).GetField("mainwindow", Static)!.GetValue(null);
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, null);
			try { Call(data, "LoadAnimations", parser); }
			finally { typeof(General).GetField("mainwindow", Static)!.SetValue(null, window); }
		}

		[Theory]
		[InlineData("gzdoom")]
		[InlineData("uzdoom")]
		public void FullPathPicsHaveIndependentKeysAndSurviveReaderReuse(string engine)
		{
			Configure(engine);
			var reader = Archive(Image("textures/anim/A.png"), Image("textures/anim/B.png"), Image("textures/anim/C.png"),
				Text("ANIMDEFS", "texture A range C tics 1\ntexture \"textures/anim/A.png\" pic \"textures/anim/B.png\" tics 1 pic \"textures/anim/A.png\" tics 1"));
			ImageData previous = null;
			for(int reload = 0; reload < 2; reload++)
			{
				if(reload != 0) using(var pool = new ResourceReaderPool())
				{
					Assert.True(pool.TryAdd(reader, null));
					Assert.True(pool.TryTake(reader.Location, out DataReader reused));
					Assert.Same(reader, reused);
				}
				DataManager data = Load(reader);
				var parser = new AnimdefsParser();
				Assert.True(parser.Parse(new TextResourceData(reader, reader.LoadFile("ANIMDEFS"), "ANIMDEFS", true), true));
				Animate(data, parser);
				ImageData shorta = data.GetTextureImage("A"), fulla = data.GetTextureImage("textures/anim/A.png");
				ImageData shortb = data.GetTextureImage("B"), fullb = data.GetTextureImage("textures/anim/B.png");
				Assert.False(ReferenceEquals(shorta, fulla));
				Assert.False(ReferenceEquals(previous, fulla));
				previous = fulla;
				var keys = Get<Dictionary<ImageData, int>>(data, "animationkeys");
				var images = Get<Dictionary<int, ImageData>>(data, "animationimages");
				Assert.True(keys[shorta] != keys[fulla]);
				Assert.Equal(2, data.Animator.Count);
				Assert.Same(fullb, images[data.Animator.Resolve(keys[fulla])]);
				Assert.Equal(keys[shortb], Get<TextureAnimationOrder>(data, "animationorder").Frame(keys[shorta], 2));
				Assert.Null(Get<TextureAnimationOrder>(data, "animationorder").Frame(keys[fulla], 2));
				data.Animator.Update(0); data.Animator.Update(28);
				Assert.Same(fulla, images[data.Animator.Resolve(keys[fulla])]);
				Assert.Same(shortb, images[data.Animator.Resolve(keys[shorta])]);
				Assert.Equal(0, map.Logger.ErrorsCount);
			}
		}

		[Theory]
		[InlineData("textures/anim/A.png", "B", "textures/anim/C.png")]
		[InlineData("A", "textures/anim/B.png", "C")]
		[InlineData("graphics/A.png", "graphics/B.png", "B")]
		public void PicsResolveMixedShortFullAndOnDemandPaths(string basetexture, string first, string second)
		{
			var reader = Archive(Image("textures/anim/A.png"), Image("textures/anim/B.png"), Image("textures/anim/C.png"),
				Image("graphics/A.png"), Image("graphics/B.png"),
				Text("ANIMDEFS", "texture \"" + basetexture + "\" pic \"" + first + "\" tics 1 pic \"" + second + "\" tics 1"));
			DataManager data = Load(reader);
			var parser = new AnimdefsParser();
			Assert.True(parser.Parse(new TextResourceData(reader, reader.LoadFile("ANIMDEFS"), "ANIMDEFS", true), true));
			Animate(data, parser);
			var keys = Get<Dictionary<ImageData, int>>(data, "animationkeys");
			var images = Get<Dictionary<int, ImageData>>(data, "animationimages");
			int key = keys[data.GetTextureImage(basetexture)];
			Assert.Equal(1, data.Animator.Count);
			Assert.Same(data.GetTextureImage(first), images[data.Animator.Resolve(key)]);
			data.Animator.Update(0); data.Animator.Update(28);
			Assert.Same(data.GetTextureImage(second), images[data.Animator.Resolve(key)]);
			Assert.Equal(0, map.Logger.ErrorsCount);
		}

		[Fact]
		public void TexturesDefinitionWinsOverSameNamedDirectoryImageForMaterialsAndGeometry()
		{
			var reader = Archive(Image("textures/MATCH.png"), Text("TEXTURES", "Texture MATCH, 64, 64 {}"));
			DataManager data = Load(reader);
			ImageData effective = data.GetTextureImage("MATCH");
			Assert.True(effective is TEXTURESImage);
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, true));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, false));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Any, false));
			Assert.False(ReferenceEquals(effective, data.GetTextureImage("textures/MATCH.png")));
		}

		[Fact]
		public void LaterEmbeddedWadKeepsItsOuterResourceAndChildRegistrationPrecedence()
		{
			string old = Wad("WallTexture MATCH, 32, 32 {}");
			var oldreader = new WADReader(new DataLocation(DataLocation.RESOURCE_WAD, old, false, false, false, null), General.Map.Config, true);
			readers.Add(oldreader);
			var reader = Archive(new KeyValuePair<string, byte[]>("first.wad", File.ReadAllBytes(Wad("WallTexture MATCH, 64, 64 {}"))),
				new KeyValuePair<string, byte[]>("last.wad", File.ReadAllBytes(Wad("WallTexture MATCH, 128, 128 {}"))));
			DataManager data = Load(oldreader, reader);
			ImageData effective = data.GetTextureImage("MATCH");
			Assert.Equal(128, effective.Width);
			Assert.Same(reader.Wads[1], effective.AnimationSource);
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, true));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, false));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Any, false));
		}

		[Fact]
		public void EmbeddedFilePrecedenceComesBeforeTheUseTypeGroup()
		{
			var reader = Archive(new KeyValuePair<string, byte[]>("first.wad", File.ReadAllBytes(Wad("Texture MATCH, 64, 64 {}"))),
				new KeyValuePair<string, byte[]>("last.wad", File.ReadAllBytes(Wad("WallTexture MATCH, 128, 128 {}"))),
				Text("TEXTURES", "Texture MATCH, 32, 32 {}"));
			DataManager data = Load(reader);
			ImageData effective = data.GetTextureImage("MATCH");
			Assert.Equal(128, effective.Width);
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Any, false));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, true));
			Assert.Same(effective, Target(data, "MATCH", MaterialTarget.Texture, false));
		}

		[Fact]
		public void TypedMaterialLookupStillPrefersWallOverGenericOverride()
		{
			var reader = Archive(Text("TEXTURES", "WallTexture MATCH, 32, 32 {}\nTexture MATCH, 64, 64 {}"));
			DataManager data = Load(reader);
			ImageData wall = Target(data, "MATCH", MaterialTarget.Texture, false);
			Assert.Equal(0, wall.AnimationUseType);
			Assert.Equal(32, wall.Width);
			Assert.Equal(2, Target(data, "MATCH", MaterialTarget.Texture, true).AnimationUseType);
			Assert.Same(data.GetTextureImage("MATCH"), Target(data, "MATCH", MaterialTarget.Texture, true));
		}

		[Theory]
		[InlineData("Zandronum_DoomUDMF.cfg", true)]
		[InlineData("GZDoom_DoomUDMF.cfg", false)]
		[InlineData("ZDaemon_DoomHexen.cfg", true)]
		public void IncludedSourcePortSelectsMergedDefaultsWithoutChangingNamespace(string filename, bool merged)
		{
			string configs = Path.Combine(Path.GetDirectoryName(typeof(General).Assembly.Location)!, "Configurations");
			var config = new Configuration(Path.Combine(configs, filename), true);
			Assert.False(config.ErrorResult, config.ErrorDescription);
			object window = typeof(General).GetField("mainwindow", Static)!.GetValue(null);
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, null);
			try { Set(General.Map, "config", new GameConfiguration(config)); }
			finally { typeof(General).GetField("mainwindow", Static)!.SetValue(null, window); }
			if(filename.Contains("UDMF")) Assert.Equal("zdoom", General.Map.Config.EngineName);
			var reader = Archive(Text("TEXTURES", "WallTexture LAVAFL1, 8, 8 {}\nWallTexture LAVAFL2, 8, 8 {}\nWallTexture LAVAFL3, 8, 8 {}"));
			DataManager data = Load(reader);
			Call(data, "LoadAnimations", (object)null);
			Assert.Equal(merged ? 1 : 0, data.Animator.Count);
			var keys = Get<Dictionary<ImageData, int>>(data, "animationkeys");
			var images = Get<Dictionary<int, ImageData>>(data, "animationimages");
			ImageData first = data.GetTextureImage("LAVAFL1");
			data.Animator.Update(0); data.Animator.Update(171);
			Assert.Same(merged ? data.GetTextureImage("LAVAFL2") : first, images[data.Animator.Resolve(keys[first])]);
		}
	}
}
