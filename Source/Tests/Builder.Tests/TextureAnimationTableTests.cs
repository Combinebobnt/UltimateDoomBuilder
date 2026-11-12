using System.Reflection;
using System.Runtime.CompilerServices;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class TextureAnimationTableTests
	{
		private sealed class Image : ImageData
		{
			internal Image(string value, int type)
			{
				name = value; longname = CodeImp.DoomBuilder.IO.Lump.MakeLongName(value); AnimationUseType = type;
			}
			protected override LocalLoadResult LocalLoadImage() { throw new NotSupportedException(); }
		}

		private static void Set(object target, string name, object value)
		{
			target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
		}

		private static DataManager Data(out TextureAnimationOrder order, params Image[] images)
		{
			var data = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
			order = new TextureAnimationOrder();
			Set(data, "animator", new TextureAnimator()); Set(data, "animationorder", order);
			Set(data, "textures", images.Where(i => i.AnimationUseType != 1).ToDictionary(i => i.LongName, i => (ImageData)i));
			Set(data, "flats", images.Where(i => i.AnimationUseType == 1).ToDictionary(i => i.LongName, i => (ImageData)i));
			Set(data, "texturenamesshorttofull", new Dictionary<long, long>()); Set(data, "flatnamesshorttofull", new Dictionary<long, long>());
			Set(data, "animationimages", images.ToDictionary(i => i.GetHashCode(), i => (ImageData)i));
			Set(data, "animationkeys", images.ToDictionary(i => (ImageData)i, i => i.GetHashCode()));
			Set(data, "warps", new Dictionary<ImageData, WarpInfo>());
			Set(data, "containers", new List<DataReader>());
			return data;
		}

		[Fact]
		public void BackwardAnimatedEntryMakesSubsequentEntryBackward()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var images = new[] { new Image("A", 0), new Image("B", 0), new Image("C", 0), new Image("D", 0), new Image("E", 0), new Image("F", 0) };
				TextureAnimationOrder order;
				DataManager data = Data(out order, images);
				object source = new object();
				foreach(Image image in images) order.Register(image.GetHashCode(), source, 0);
				order.Seal();
				data.AddAnimatedEntries(new[] { new AnimatedEntry { IsTexture = true, First = "C", Last = "A", Tics = 1 }, new AnimatedEntry { IsTexture = true, First = "D", Last = "F", Tics = 1 } });
				data.Animator.Update(0); data.Animator.Update(28);
				Assert.Equal(2, data.Animator.Count);
				Assert.Equal(images[2].GetHashCode(), data.Animator.Resolve(images[0].GetHashCode()));
				Assert.Equal(images[5].GetHashCode(), data.Animator.Resolve(images[3].GetHashCode()));
			}
		}

		[Fact]
		public void CrossReaderAndUseTypeRangesAreDroppedAndSwirlIsKept()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var a = new Image("A", 0); var b = new Image("B", 0); var c = new Image("C", 2);
				TextureAnimationOrder order;
				DataManager data = Data(out order, a, b, c);
				object source = new object();
				order.Register(a.GetHashCode(), source, 0); order.Register(b.GetHashCode(), new object(), 0); order.Register(c.GetHashCode(), source, 2); order.Seal();
				data.AddAnimatedEntries(new[] { new AnimatedEntry { IsTexture = true, First = "A", Last = "B", Tics = 1 }, new AnimatedEntry { IsTexture = true, First = "A", Last = "C", Tics = 1 } });
				Assert.Equal(0, data.Animator.Count);
				data.AddAnimatedEntries(new[] { new AnimatedEntry { IsTexture = true, First = "A", Last = "C", Tics = 65536 } });
				Assert.Equal(2, data.GetWarp(a).Value.Style);
				Assert.Equal(0, data.Animator.Count);
			}
		}

		[Fact]
		public void AnimdefsOverridesDefaultAndUnknownFrameDropsOnlyItsDefinition()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				Set(General.Map.Config, "enginename", "gzdoom");
				var images = new[] { new Image("NUKAGE1", 1), new Image("NUKAGE2", 1), new Image("NUKAGE3", 1) };
				TextureAnimationOrder order;
				DataManager data = Data(out order, images);
				object source = new object();
				foreach(Image image in images) order.Register(image.GetHashCode(), source, 1);
				order.Seal();
				var parser = new AnimdefsParser();
				parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("texture NUKAGE2 pic ABSENT tics 1 pic NUKAGE3 tics 1\ntexture NUKAGE1 pic NUKAGE3 tics 1 pic NUKAGE2 tics 1\nwarp flat NUKAGE1 2\nwarp2 flat NUKAGE1 3"), true);
				typeof(DataManager).GetMethod("LoadAnimations", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(data, new object[] { parser });
				Assert.Equal(1, data.Animator.Count);
				Assert.Equal(images[2].GetHashCode(), data.Animator.Resolve(images[0].GetHashCode()));
				Assert.Equal(1, map.Logger.ErrorsCount);
				Assert.Equal(1, data.GetWarp(images[0]).Value.Style);
				Assert.Equal(3.0, data.GetWarp(images[0]).Value.Speed);
			}
		}

		[Fact]
		public void IndexedReadinessQueuesMissingDataWithoutReleasingTheColourTexture()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				TextureAnimationOrder order;
				DataManager data = Data(out order);
				Set(data, "syncobject", new object());
				var queue = new Queue<ImageData>();
				Set(data, "imageque", queue);
				Set(General.Map, "data", data);
				var image = new Image("A", 0) { ImageState = ImageLoadState.Ready };
				var texture = (CodeImp.DoomBuilder.Rendering.Texture)RuntimeHelpers.GetUninitializedObject(typeof(CodeImp.DoomBuilder.Rendering.Texture));
				GC.SuppressFinalize(texture);
				typeof(ImageData).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, texture);
				Assert.False(image.IsReadyFor(true));
				Assert.Equal(ImageLoadState.Loading, image.ImageState);
				Assert.Equal(1, queue.Count);
				Assert.True(ReferenceEquals(texture, typeof(ImageData).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(image)));
				Assert.True(image.IsReadyFor(false));
				Assert.False(image.IsReadyFor(true));
				Assert.Equal(1, queue.Count);
				typeof(ImageData).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, null);
				image.Dispose();
			}
		}

		[Fact]
		public void ReadinessRejectsFailedAndDisposedImagesWithoutQueuing()
		{
			var image = new Image("A", 0) { ImageState = ImageLoadState.Ready };
			typeof(ImageData).GetField("loadfailed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(image, true);
			Assert.False(image.IsReadyFor(false));
			Assert.False(image.IsReadyFor(true));
			image.Dispose();
			Assert.False(image.IsReadyFor(false));
		}
	}
}
