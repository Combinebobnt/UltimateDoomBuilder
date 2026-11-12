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
using CodeImp.DoomBuilder.Rendering;
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
		private readonly object previoussettings = typeof(General).GetField("settings", Static)!.GetValue(null);
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
			typeof(General).GetField("settings", Static)!.SetValue(null, new ProgramConfiguration { GZAnimateTextures = false, GZDrawMaterials = true });
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
					.Concat(Get<Dictionary<string, ImageData>>(data, "materialimages").Values)
					.Concat(Get<Dictionary<ImageData, ImageData>>(data, "shortmaterialimages").Keys)
					.Concat(Get<Dictionary<ImageData, ImageData>>(data, "shortmaterialimages").Values).Distinct();
				foreach(ImageData image in images) if(image != null) image.Dispose();
			}
			foreach(DataReader reader in readers) reader.Dispose();
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, previouswindow);
			typeof(General).GetField("temppath", Static)!.SetValue(null, previoustemp);
			typeof(General).GetField("thisasm", Static)!.SetValue(null, previousassembly);
			typeof(General).GetField("scriptconfigs", Static)!.SetValue(null, previousscripts);
			typeof(General).GetField("settings", Static)!.SetValue(null, previoussettings);
			graphics.Dispose(); bitmap.Dispose(); map.Dispose();
			Directory.Delete(directory, true);
		}

		private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance)!.SetValue(target, value);
		private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance)!.GetValue(target);
		private static object Call(DataManager target, string name, params object[] args) => typeof(DataManager).GetMethod(name, Instance)!.Invoke(target, args);

		private void Configure(string engine, bool mix = false, bool decorate = false)
		{
			var config = new Configuration();
			Assert.True(config.InputConfiguration("basegame = \"doom\"; engine = \"" + engine + "\"; longtexturenames = true; formatinterface = \"DoomMapSetIO\"; mixtexturesflats = " + (mix ? "true" : "false") + "; decorategames = \"" + (decorate ? "doom" : "") + "\";"));
			Set(General.Map, "config", new GameConfiguration(config));
		}

		private DataManager Load(params DataReader[] resources)
		{
			return Load(null, resources);
		}

		private DataManager Load(Action<DataManager> beforeseal, params DataReader[] resources)
		{
			var data = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
			Set(data, "containers", resources.ToList());
			Set(data, "animator", new TextureAnimator()); Set(data, "animationorder", new TextureAnimationOrder());
			Set(data, "animationimages", new Dictionary<int, ImageData>()); Set(data, "animationkeys", new Dictionary<ImageData, int>());
			Set(data, "materialregistrations", Activator.CreateInstance(typeof(DataManager).GetField("materialregistrations", Instance)!.FieldType));
			Set(data, "warps", new Dictionary<ImageData, WarpInfo>());
			Set(data, "shortmaterialimages", new Dictionary<ImageData, ImageData>());
			Set(data, "fullmaterialimages", new Dictionary<string, ImageData>(StringComparer.OrdinalIgnoreCase));
			Set(data, "materials", new Dictionary<ImageData, GldefsMaterial>());
			Set(data, "resolvedmaterials", new Dictionary<ImageData, ResolvedMaterial>());
			Set(data, "resolvedwarpmaterials", new Dictionary<ImageData, ResolvedMaterial>());
			Set(data, "materialimages", new Dictionary<string, ImageData>(StringComparer.OrdinalIgnoreCase));
			Set(data, "materialprograms", new Dictionary<string, UserShaderProgram>());
			Set(data, "sprites", new Dictionary<long, ImageData>());
			Set(data, "syncobject", new object()); Set(data, "imageque", new Queue<ImageData>());
			Set(data, "textures", new Dictionary<long, ImageData>()); Set(data, "flats", new Dictionary<long, ImageData>());
			Set(data, "texturenamesshorttofull", new Dictionary<long, long>()); Set(data, "flatnamesshorttofull", new Dictionary<long, long>());
			Set(data, "scriptresources", new Dictionary<ScriptType, HashSet<ScriptResource>>());
			Set(General.Map, "data", data);
			var parsers = new Dictionary<string, TexturesParser>();
			Call(data, "LoadTextures", Get<Dictionary<long, ImageData>>(data, "textures"), Get<Dictionary<long, long>>(data, "texturenamesshorttofull"), parsers);
			Call(data, "LoadFlats", Get<Dictionary<long, ImageData>>(data, "flats"), Get<Dictionary<long, long>>(data, "flatnamesshorttofull"), parsers);
			managers.Add(data);
			if(General.Map.Config.MixTexturesFlats) foreach(DataReader reader in resources)
			{
				reader.TextureSet.MixTexturesAndFlats();
				foreach(ImageData image in reader.TextureSet.Textures) Get<Dictionary<long, ImageData>>(data, "textures")[image.LongName] = image;
				foreach(ImageData image in reader.TextureSet.Flats) Get<Dictionary<long, ImageData>>(data, "flats")[image.LongName] = image;
			}
			beforeseal?.Invoke(data);
			Get<TextureAnimationOrder>(data, "animationorder").Seal();
			Call(data, "SplitMaterialImageIdentities");
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
		private static KeyValuePair<string, byte[]> Image(string path, int width = 8, int height = 8)
		{
			using(var image = new Bitmap(width, height))
			using(var stream = new MemoryStream())
			{
				using(var graphics = Graphics.FromImage(image)) graphics.Clear(Color.White);
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

		private static void ReadyPixels(ImageData image)
		{
			// Decode real reader bytes without the window's asynchronous image-delivery machinery.
			Bitmap pixels = image.LocalGetBitmap(false);
			Assert.NotNull(pixels);
			Assert.Equal(image.Width, pixels.Width); Assert.Equal(image.Height, pixels.Height);
			Assert.Equal(Color.White.ToArgb(), pixels.GetPixel(0, 0).ToArgb());
			typeof(ImageData).GetField("loadedbitmap", Instance)!.SetValue(image, pixels);
			image.ImageState = ImageLoadState.Ready;
		}

		private static void ApplyMaterials(DataManager data, DataReader reader)
		{
			var parser = new GldefsParser();
			Assert.True(parser.Parse(new TextResourceData(reader, reader.LoadFile("GLDEFS"), "GLDEFS", true), true), parser.ErrorDescription);
			var materials = Get<Dictionary<ImageData, GldefsMaterial>>(data, "materials");
			foreach(GldefsMaterial definition in parser.Materials)
			{
				ImageData image = (ImageData)Call(data, "FindMaterialTarget", definition);
				Assert.NotNull(image);
				if(!materials.TryGetValue(image, out GldefsMaterial material)) materials[image] = material = new GldefsMaterial();
				material.Merge(definition);
			}
		}

		[Theory]
		[InlineData("zandronum", false)]
		[InlineData("gzdoom", true)]
		[InlineData("uzdoom", true)]
		[InlineData("zdoom", true)]
		public void WarpOverridesHardwareShaderWithoutChangingTheUnanimatedMaterial(string engine, bool bright)
		{
			Configure(engine, false, true);
			var reader = Archive(Image("textures/WARP.png"), Image("brightmaps/warp.png"),
				Text("GLDEFS", "HardwareShader texture WARP { shader \"shaders/missing.fp\" texture detail \"missing.png\" }\nBrightmap texture WARP { map \"brightmaps/warp.png\" }"));
			DataManager data = Load(reader);
			ApplyMaterials(data, reader);
			ImageData image = data.GetTextureImage("WARP");
			Call(data, "SetWarp", image, 2, -1.5);
			var dialect = UserShaderBuilder.WarpDialect(engine);
			var program = UserShaderBuilder.BuildWarp(2, dialect);
			Get<Dictionary<string, UserShaderProgram>>(data, "materialprograms")[program.Path] = program;
			ResolvedMaterial warped = data.GetMaterial(image, true);
			Assert.Same(program, warped.Program);
			Assert.Equal(-1.5f, warped.Definition.Speed);
			Assert.Equal(bright, warped.Brightmap != null);
			Assert.Empty(warped.Textures);
			Assert.False(warped.Definition.DisableAlphaTest);
			Assert.Equal(0, map.Logger.ErrorsCount);
			Assert.Same(warped, data.GetMaterial(image, true));
			Call(data, "LoadMaterialImage", "brightmaps/warp.png");
			object window = typeof(General).GetField("mainwindow", Static)!.GetValue(null);
			typeof(General).GetField("mainwindow", Static)!.SetValue(null, null);
			ResolvedMaterial plain;
			try { plain = data.GetMaterial(image); }
			finally { typeof(General).GetField("mainwindow", Static)!.SetValue(null, window); }
			Assert.False(ReferenceEquals(plain, warped));
			Assert.Equal("shaders/missing.fp", plain.Definition.Shader);
			Assert.Null(plain.Program);
			Assert.Same(plain, data.GetMaterial(image, false));
			Assert.Equal(1, map.Logger.ErrorsCount);
		}

		[Fact]
		public void WarpSelectionFollowsTheReadyTranslatedFrame()
		{
			Configure("gzdoom", false, true);
			var reader = Archive(Image("textures/A.png"), Image("textures/B.png"),
				Text("ANIMDEFS", "texture A pic B tics 4 pic A tics 4\nwarp texture B"));
			DataManager data = Load(reader);
			var parser = new AnimdefsParser();
			Assert.True(parser.Parse(new TextResourceData(reader, reader.LoadFile("ANIMDEFS"), "ANIMDEFS", true), true));
			Animate(data, parser);
			ImageData a = data.GetTextureImage("A"), b = data.GetTextureImage("B");
			ReadyPixels(b);
			var program = UserShaderBuilder.BuildWarp(1, UserShaderDialect.UZDoom);
			Get<Dictionary<string, UserShaderProgram>>(data, "materialprograms")[program.Path] = program;
			Assert.Null(data.GetMaterial(a, true));
			Assert.Same(b, data.ResolveAnimatedImage(a, false));
			Assert.Same(program, data.GetMaterial(data.ResolveAnimatedImage(a, false), true).Program);
			Assert.Null(data.GetMaterial(b));
		}

		[Fact]
		public void SwirlMetadataDoesNotEnableWarpInVanillaConfiguration()
		{
			Configure("doom2");
			DataManager data = Load(Archive(Image("textures/SWIRL.png")));
			ImageData image = data.GetTextureImage("SWIRL");
			Call(data, "SetWarp", image, 2, 1.0);
			Assert.NotNull(data.GetWarp(image));
			Assert.Null(data.GetMaterial(image, true));
		}

		[Fact]
		public void SharedHiResImageKeepsExplicitShaderAndBrightmapOnWallAndFloorWithoutAnimation()
		{
			Configure("gzdoom", true);
			var reader = Archive(Image("patches/base.png"), Image("hires/SHARED.png", 32, 32), Image("brightmaps/shared.png"),
				Text("TEXTURES", "WallTexture SHARED, 16, 8 { XScale 2 YScale 4 Offset 3, 5 Patch \"patches/base.png\", 0, 0 }"),
				Text("GLDEFS", "HardwareShader texture SHARED { shader \"shaders/shared.fp\" }\nBrightmap texture SHARED { map \"brightmaps/shared.png\" }"),
				Text("shaders/shared.fp", "vec4 Process(vec4 color) { return color; }"));
			ImageData original = null;
			int key = -1;
			DataManager data = Load(value => {
				original = value.GetTextureImage("SHARED");
				Assert.Same(original, value.GetFlatImage("SHARED"));
				ReadyPixels(original);
				key = Get<Dictionary<ImageData, int>>(value, "animationkeys")[original];
				Assert.Equal(2, (int)Call(value, "LoadHiResTextures"));
			}, reader);
			ImageData wall = data.GetTextureImage("SHARED"), floor = data.GetFlatImage("SHARED");
			Assert.False(General.Settings.GZAnimateTextures);
			Assert.Equal(0, data.Animator.Count);
			ApplyMaterials(data, reader);
			ResolvedMaterial wallmaterial = data.GetMaterial(wall), floormaterial = data.GetMaterial(floor);
			Assert.True(wallmaterial != null, "Wall lost its explicit GLDEFS material after HiRes replacement");
			Assert.True(floormaterial != null, "Floor lost its explicit GLDEFS material after shared HiRes replacement");
			Assert.Same(wallmaterial, floormaterial);
			Assert.Same(wall, floor);
			Assert.True(wall is HiResImage);
			Assert.False(ReferenceEquals(original, wall));
			Assert.Equal("shaders/shared.fp", floormaterial.Definition.Shader);
			Assert.Equal("brightmaps/shared.png", floormaterial.Definition.Brightmap);
			Assert.NotNull(floormaterial.Brightmap);
			using(var pixels = floormaterial.Brightmap.LocalGetBitmap(false))
			{
				Assert.Equal(8, pixels.Width);
				Assert.Equal(Color.White.ToArgb(), pixels.GetPixel(0, 0).ToArgb());
			}
			using(var pixels = wall.LocalGetBitmap(false)) Assert.Equal(32, pixels.Width);
			Assert.Equal(16, wall.Width); Assert.Equal(8, wall.Height);
			Assert.Equal(original.Scale, wall.Scale);
			Assert.Equal(3, wall.OffsetX); Assert.Equal(5, wall.OffsetY);
			Assert.Equal(original.TextureNamespace, wall.TextureNamespace);
			Assert.Equal(key, Get<Dictionary<ImageData, int>>(data, "animationkeys")[wall]);
			Assert.Same(wall, Get<Dictionary<int, ImageData>>(data, "animationimages")[key]);
			Assert.Equal(key, Get<TextureAnimationOrder>(data, "animationorder").Frame(key, 1));
			Assert.Equal(0, map.Logger.ErrorsCount);
		}

		[Fact]
		public void DistinctWallAndFlatHiResImagesKeepTheirOwnSettingsAndExplicitMaterials()
		{
			Configure("gzdoom", true);
			var reader = Archive(Image("patches/base.png"), Image("hires/SPLIT.png", 32, 32), Image("brightmaps/wall.png"), Image("brightmaps/floor.png"),
				Text("TEXTURES", "WallTexture SPLIT, 16, 8 { XScale 2 Patch \"patches/base.png\", 0, 0 }\nFlat SPLIT, 8, 16 { YScale 4 Patch \"patches/base.png\", 0, 0 }"),
				Text("GLDEFS", "HardwareShader texture SPLIT { shader \"shaders/wall.fp\" }\nBrightmap texture SPLIT { map \"brightmaps/wall.png\" }\nHardwareShader flat SPLIT { shader \"shaders/floor.fp\" }\nBrightmap flat SPLIT { map \"brightmaps/floor.png\" }"),
				Text("shaders/wall.fp", "vec4 Process(vec4 color) { return color; }"), Text("shaders/floor.fp", "vec4 Process(vec4 color) { return color; }"));
			ImageData oldwall = null, oldfloor = null;
			int wallkey = -1, floorkey = -1;
			DataManager data = Load(value => {
				oldwall = value.GetTextureImage("SPLIT"); oldfloor = value.GetFlatImage("SPLIT");
				Assert.False(ReferenceEquals(oldwall, oldfloor));
				ReadyPixels(oldwall); ReadyPixels(oldfloor);
				wallkey = Get<Dictionary<ImageData, int>>(value, "animationkeys")[oldwall];
				floorkey = Get<Dictionary<ImageData, int>>(value, "animationkeys")[oldfloor];
				Assert.Equal(2, (int)Call(value, "LoadHiResTextures"));
			}, reader);
			ImageData wall = data.GetTextureImage("SPLIT"), floor = data.GetFlatImage("SPLIT");
			Assert.False(ReferenceEquals(wall, floor));
			Assert.True(wall is HiResImage && floor is HiResImage);
			Assert.Equal(16, wall.Width); Assert.Equal(8, wall.Height);
			Assert.Equal(8, floor.Width); Assert.Equal(16, floor.Height);
			Assert.Equal(oldwall.Scale, wall.Scale); Assert.Equal(oldfloor.Scale, floor.Scale);
			Assert.Equal(TextureNamespace.WALLTEXTURE, wall.TextureNamespace); Assert.Equal(TextureNamespace.FLAT, floor.TextureNamespace);
			Assert.Equal(wallkey, Get<Dictionary<ImageData, int>>(data, "animationkeys")[wall]);
			Assert.Equal(floorkey, Get<Dictionary<ImageData, int>>(data, "animationkeys")[floor]);
			Assert.Same(wall, Get<Dictionary<int, ImageData>>(data, "animationimages")[wallkey]);
			Assert.Same(floor, Get<Dictionary<int, ImageData>>(data, "animationimages")[floorkey]);
			ApplyMaterials(data, reader);
			ResolvedMaterial wallmaterial = data.GetMaterial(wall), floormaterial = data.GetMaterial(floor);
			Assert.NotNull(wallmaterial); Assert.NotNull(floormaterial);
			Assert.False(ReferenceEquals(wallmaterial, floormaterial));
			Assert.Equal("shaders/wall.fp", wallmaterial.Definition.Shader);
			Assert.Equal("shaders/floor.fp", floormaterial.Definition.Shader);
			Assert.Equal("brightmaps/wall.png", wallmaterial.Definition.Brightmap);
			Assert.Equal("brightmaps/floor.png", floormaterial.Definition.Brightmap);
			Assert.NotNull(wallmaterial.Brightmap); Assert.NotNull(floormaterial.Brightmap);
			Assert.False(General.Settings.GZAnimateTextures);
			Assert.Equal(0, data.Animator.Count);
			Assert.Equal(0, map.Logger.ErrorsCount);
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
