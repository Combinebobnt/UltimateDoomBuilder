using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	public class TextureAnimatorTests
	{
		[Fact]
		public void AnimationIsEnabledInDefaultConfiguration()
		{
			Assert.True(new CodeImp.DoomBuilder.Config.ProgramConfiguration().GZAnimateTextures);
		}

		private static AnimDef Range(AnimationType type = AnimationType.Forward, int duration = 10)
		{
			return new AnimDef(new[] { 10, 20, 30 }, new[] { duration }, new[] { duration }, false, type);
		}

		[Fact]
		public void FirstUpdateArmsAndRangeMembersKeepTheirPhase()
		{
			var animator = new TextureAnimator();
			animator.Add(10, Range());
			animator.Update(100);
			Assert.Equal(10, animator.Resolve(10));
			Assert.Equal(20, animator.Resolve(20));
			animator.Update(109);
			Assert.Equal(10, animator.Resolve(10));
			animator.Update(110);
			Assert.Equal(20, animator.Resolve(10));
			Assert.Equal(30, animator.Resolve(20));
			Assert.Equal(10, animator.Resolve(30));
			Assert.Equal(99, animator.Resolve(99));
		}

		[Fact]
		public void LateUpdatesAdvanceOnceAndRescheduleFromNow()
		{
			var animator = new TextureAnimator();
			animator.Add(10, Range());
			animator.Update(0);
			animator.Update(10000);
			Assert.Equal(20, animator.Resolve(10));
			animator.Update(10000);
			Assert.Equal(20, animator.Resolve(10));
			animator.Update(10010);
			Assert.Equal(30, animator.Resolve(10));
		}

		[Fact]
		public void BackwardWrapsAndOscillationBouncesAtBothEnds()
		{
			var backward = new TextureAnimator();
			backward.Add(10, Range(AnimationType.Backward));
			backward.Update(0); backward.Update(10);
			Assert.Equal(30, backward.Resolve(10));
			backward.Update(20);
			Assert.Equal(20, backward.Resolve(10));
			var oscillate = new TextureAnimator();
			oscillate.Add(10, Range(AnimationType.OscillateUp));
			oscillate.Update(0);
			int[] expected = { 20, 30, 20, 10, 20 };
			for(int i = 0; i < expected.Length; i++) { oscillate.Update((i + 1) * 10); Assert.Equal(expected[i], oscillate.Resolve(10)); }
		}

		[Fact]
		public void ReversedOscillationPreservesTheEnginesUnsignedInitialStep()
		{
			var animator = new TextureAnimator();
			AnimDef definition = Range(AnimationType.OscillateDown);
			animator.Add(10, definition);
			animator.Update(0); animator.Update(10);
			Assert.Equal(65535, definition.CurrentFrame);
			Assert.Equal(10, animator.Resolve(10));
			animator.Update(20);
			Assert.Equal(30, animator.Resolve(10));
		}

		[Fact]
		public void RandomNeverRepeatsTheCurrentFrame()
		{
			var animator = new TextureAnimator(new Random(42));
			animator.Add(10, Range(AnimationType.Random));
			animator.Update(0);
			for(int i = 1; i <= 100; i++)
			{
				int previous = animator.Resolve(10);
				animator.Update(i * 10);
				Assert.False(previous == animator.Resolve(10));
			}
		}

		[Fact]
		public void DiscreteAnimationUsesEachFramesDurationAndOnlyTranslatesTheBase()
		{
			var animator = new TextureAnimator();
			animator.Add(99, new AnimDef(new[] { 10, 20, 30 }, new[] { 10, 20, 30 }, new[] { 10, 20, 30 }, true, AnimationType.Forward));
			animator.Update(0); animator.Update(10);
			Assert.Equal(20, animator.Resolve(99));
			Assert.Equal(20, animator.Resolve(20));
			animator.Update(29);
			Assert.Equal(20, animator.Resolve(99));
			animator.Update(30);
			Assert.Equal(30, animator.Resolve(99));
			animator.Update(60);
			Assert.Equal(10, animator.Resolve(99));
		}

		[Fact]
		public void RandomDurationUsesAnExclusiveRange()
		{
			var animator = new TextureAnimator(new Random(3));
			var definition = new AnimDef(new[] { 10, 20 }, new[] { 10 }, new[] { 20 }, false, AnimationType.Forward);
			animator.Add(10, definition);
			for(int i = 0; i < 100; i++)
			{
				long now = i * 100;
				animator.Update(now);
				Assert.True(definition.SwitchTime >= now + 10 && definition.SwitchTime < now + 20);
			}
		}

		[Theory]
		[InlineData(0)]
		[InlineData(-10)]
		public void InvalidDurationClampsAndCannotSpin(int duration)
		{
			var animator = new TextureAnimator();
			int warnings = 0;
			animator.Add(10, Range(duration: duration), message => warnings++);
			animator.Update(0); animator.Update(100000);
			Assert.Equal(1, warnings);
			Assert.Equal(20, animator.Resolve(10));
		}

		[Fact]
		public void BackwardClockAndExplicitRearmKeepTheCurrentFrame()
		{
			var animator = new TextureAnimator();
			animator.Add(10, Range());
			animator.Update(100); animator.Update(110);
			animator.Update(0);
			Assert.Equal(20, animator.Resolve(10));
			animator.Update(9);
			Assert.Equal(20, animator.Resolve(10));
			animator.Update(10);
			Assert.Equal(30, animator.Resolve(10));
			animator.Rearm(); animator.Update(500);
			Assert.Equal(30, animator.Resolve(10));
			animator.Update(510);
			Assert.Equal(10, animator.Resolve(10));
		}

		[Fact]
		public void LaterDefinitionReplacesSameBaseAndRemovesOldMemberTranslations()
		{
			var animator = new TextureAnimator();
			animator.Add(10, Range());
			animator.Update(0); animator.Update(10);
			animator.Add(10, new AnimDef(new[] { 50, 60 }, new[] { 10, 10 }, new[] { 10, 10 }, true, AnimationType.Forward));
			Assert.Equal(1, animator.Count);
			Assert.Equal(50, animator.Resolve(10));
			Assert.Equal(20, animator.Resolve(20));
		}

		[Fact]
		public void ReaderOrderIsStableWithinUseTypeAndNeverCrossesSources()
		{
			var order = new TextureAnimationOrder();
			object reader = new object(), other = new object();
			order.Register(70, reader, 2); order.Register(30, reader, 0); order.Register(10, reader, 0);
			order.Register(60, reader, 1); order.Register(20, reader, 0); order.Register(80, reader, 2);
			order.Register(90, other, 0);
			order.Seal();
			bool backward;
			Assert.Equal(new[] { 30, 10, 20 }, order.Range(30, 20, out backward));
			Assert.False(backward);
			Assert.Equal(new[] { 30, 10, 20 }, order.Range(20, 30, out backward));
			Assert.True(backward);
			Assert.Null(order.Range(30, 90, out backward));
			Assert.Null(order.Range(30, 70, out backward));
			Assert.Equal(10, order.Frame(30, 2).Value);
			Assert.Equal(60, order.Frame(30, 4).Value);
			Assert.Null(order.Frame(30, 100));
		}

		[Fact]
		public void ArchiveOrderUsesCaseInsensitiveNormalizedFullPaths()
		{
			string[] files = { "textures/z/A.png", "textures/B/z.png", "Textures/a/Z.png", "textures/a/A.png" };
			Array.Sort(files, PK3StructuredReader.CompareAnimationPaths);
			Assert.Equal(new[] { "textures/a/A.png", "Textures/a/Z.png", "textures/B/z.png", "textures/z/A.png" }, files);
			Assert.Equal(0, PK3StructuredReader.CompareAnimationPaths("Textures\\A.png", "textures/a.png"));
		}
	}
}
