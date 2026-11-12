#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.ZDoom
{
	public struct CameraTextureData
	{
		public string Name;
		public int Width;
		public int Height;
		public float ScaleX;
		public float ScaleY;
		public bool WorldPanning;
		public bool FitTexture;
	}
	
	internal enum AnimationKind { Range, Pic }
	internal enum AnimationType { Forward, Backward, OscillateUp, OscillateDown, Random }

	internal sealed class AnimdefsFrame
	{
		public string Name;
		public int? Number;
		public int MinMs;
		public int MaxMs;
	}

	internal sealed class AnimdefsAnimation
	{
		public string Name;
		public bool IsTexture;
		public bool Optional;
		public AnimationKind Kind;
		public AnimationType Type;
		public readonly List<AnimdefsFrame> Frames = new List<AnimdefsFrame>();
		public string Source;
		public int Line;
	}

	internal sealed class AnimdefsWarp
	{
		public string Name;
		public bool IsTexture;
		public int Style;
		public double Speed = 1.0;
		public string Source;
		public int Line;
	}

	internal sealed class AnimdefsParser : ZDTextParser
	{
		#region ================== Variables

		private readonly Dictionary<string, CameraTextureData> cameratextures;
		private readonly List<AnimdefsAnimation> animations = new List<AnimdefsAnimation>();
		private readonly List<AnimdefsWarp> warps = new List<AnimdefsWarp>();
		private long tokenposition;
		private static readonly HashSet<string> topkeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{ "texture", "flat", "warp", "warp2", "cameratexture", "switch", "animateddoor", "skyoffset", "canvastexture", "firetexture" };

		#endregion

		#region ================== Properties

		internal override ScriptType ScriptType { get { return ScriptType.ANIMDEFS; } }

		public Dictionary<string, CameraTextureData> CameraTextures { get { return cameratextures; } }
		internal IList<AnimdefsAnimation> Animations { get { return animations; } }
		internal IList<AnimdefsWarp> Warps { get { return warps; } }

		#endregion

		#region ================== Constructor

		internal AnimdefsParser()
		{
			cameratextures = new Dictionary<string, CameraTextureData>();
		}

		#endregion

		#region ================== Parsing

		public override bool Parse(TextResourceData data, bool clearerrors)
		{
			// Already parsed?
			if(!base.AddTextResource(data))
			{
				if(clearerrors) ClearError();
				return true;
			}

			// Cannot process?
			if(!base.Parse(data, clearerrors)) return false;

			// Continue until at the end of the stream
			while(SkipWhitespace(true))
			{
				string token = Next();
				if(string.IsNullOrEmpty(token)) break;
				if(!token.Equals("cameratexture", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						switch(token.ToLowerInvariant())
						{
							case "texture": case "flat": ParseAnimation(token.Equals("texture", StringComparison.OrdinalIgnoreCase)); break;
							case "warp": case "warp2": ParseWarp(token.Equals("warp2", StringComparison.OrdinalIgnoreCase) ? 2 : 1); break;
							case "switch": SkipSwitch(); break;
							case "animateddoor": SkipDoor(); break;
							case "skyoffset": Required(); Required(); break;
							case "canvastexture": SkipCanvas(); break;
							case "firetexture": SkipFire(); break;
						}
					}
					catch(FormatException e)
					{
						LogWarning(e.Message);
						SkipDefinition();
					}
					continue;
				}

				// Texture name
				SkipWhitespace(true);
				string texturename = StripQuotes(ReadToken(false));
				if(string.IsNullOrEmpty(texturename))
				{
					ReportError("Expected camera texture name");
					return false;
				}

                /*
				// Camera texture names are limited to 8 chars
				if(texturename.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH)
				{
					ReportError("Camera texture names must be no longer than " + DataManager.CLASIC_IMAGE_NAME_LENGTH + " chars");
					return false;
				}*/
                // [ZZ] not really. I've successfully tried it with >8 chars and it works.

				// Width
				int width = -1;
				SkipWhitespace(true);
				if(!ReadSignedInt(ref width) || width < 1)
				{
					ReportError("Expected camera texture width");
					return false;
				}

				// Height
				int height = -1;
				SkipWhitespace(true);
				if(!ReadSignedInt(ref height) || height < 1)
				{
					ReportError("Expected camera texture height");
					return false;
				}

				// "Fit" keyword?
				bool worldpanning = false;
				bool fit = false;
				float scalex = 1.0f;
				float scaley = 1.0f;

				if(NextTokenIs("fit", false))
				{
					fit = true;
					int fitwidth = width;
					int fitheight = height;
					
					// Fit width
					SkipWhitespace(true);
					if(!ReadSignedInt(ref fitwidth) || fitwidth < 1)
					{
						ReportError("Expected camera texture fit width");
						return false;
					}

					// Fit height
					SkipWhitespace(true);
					if(!ReadSignedInt(ref fitheight) || fitheight < 1)
					{
						ReportError("Expected camera texture fit height");
						return false;
					}

					// Update scale
					scalex = (float)fitwidth / width;
					scaley = (float)fitheight / height;

					// WorldPanning
					worldpanning = NextTokenIs("worldpanning", false);
				}
				else if(NextTokenIs("worldpanning", false))
				{
					worldpanning = true;
				}

				// Check results
				if(cameratextures.ContainsKey(texturename.ToUpperInvariant()))
				{
					ReportError("Camera texture \"" + texturename + "\" is defined more than once");
					return false;
				}

				// Store results
				texturename = texturename.ToUpperInvariant();
				cameratextures[texturename] = new CameraTextureData { Name = texturename, Width = width, Height = height, 
																	  ScaleX = scalex, ScaleY = scaley, 
																	  WorldPanning = worldpanning, FitTexture = fit };
			}

			return true;
		}

		private string Next()
		{
			if(!SkipWhitespace(true)) return string.Empty;
			tokenposition = datastream.Position;
			return ReadToken("{}\n");
		}

		private void Unread() { datastream.Position = tokenposition; }
		private bool Take(string value)
		{
			string token = Next();
			if(token.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
			if(token.Length > 0) Unread();
			return false;
		}

		private string Required()
		{
			string token = StripQuotes(Next());
			if(token.Length == 0) throw new FormatException("Unexpected end of definition");
			return token;
		}

		private double Number()
		{
			double value;
			string token = Required();
			if(!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
			{
				Unread();
				throw new FormatException("Expected a finite number");
			}
			return value;
		}

		private int Milliseconds(double tics)
		{
			double value = tics * 1000.0 / 35.0;
			if(value < 1)
			{
				LogWarning("Animation duration clamped to 1 ms");
				return 1;
			}
			return (int)Math.Min(value, int.MaxValue);
		}

		private void ReadTime(AnimdefsFrame frame)
		{
			string token = Required().ToLowerInvariant();
			if(token != "tics" && token != "rand")
			{
				Unread();
				throw new FormatException("Must specify a duration for animation frame");
			}
			frame.MinMs = Milliseconds(Number());
			frame.MaxMs = token == "rand" ? Milliseconds(Number()) : frame.MinMs;
			if(frame.MaxMs < frame.MinMs) throw new FormatException("Animation random duration maximum is less than its minimum");
		}

		private void ParseAnimation(bool texture)
		{
			var anim = new AnimdefsAnimation { IsTexture = texture, Source = sourcename, Line = GetCurrentLineNumber() };
			anim.Optional = Take("optional");
			anim.Name = Required().ToUpperInvariant();
			bool range = false, pic = false;
			for(string token = Next(); token.Length > 0; token = Next())
			{
				switch(token.ToLowerInvariant())
				{
					case "allowdecals": case "notrim": continue;
					case "oscillate":
						if(anim.Type == AnimationType.Random) throw new FormatException("Cannot combine random and oscillate");
						anim.Type = AnimationType.OscillateUp; continue;
					case "random":
						if(anim.Type == AnimationType.OscillateUp) throw new FormatException("Cannot combine random and oscillate");
						anim.Type = AnimationType.Random; continue;
					case "range": case "pic":
						bool isrange = token.Equals("range", StringComparison.OrdinalIgnoreCase);
						if((isrange && (range || pic)) || (!isrange && range)) throw new FormatException("Cannot combine pic and range or repeat range");
						range |= isrange; pic |= !isrange;
						if(isrange && anim.Name.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH) throw new FormatException("Cannot use range for long texture names");
						var frame = new AnimdefsFrame();
						string name = Required();
						int number;
						if(int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) frame.Number = number;
						else frame.Name = name.ToUpperInvariant();
						if(isrange && frame.Name != null && frame.Name.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH) throw new FormatException("Cannot use range for long texture names");
						ReadTime(frame);
						anim.Frames.Add(frame); continue;
					default: Unread(); break;
				}
				break;
			}
			if(pic && anim.Frames.Count < 2) throw new FormatException("Animation needs at least 2 frames");
			if(range || pic)
			{
				anim.Kind = range ? AnimationKind.Range : AnimationKind.Pic;
				animations.Add(anim);
			}
		}

		private void ParseWarp(int style)
		{
			int line = GetCurrentLineNumber();
			string kind = Required().ToLowerInvariant();
			if(kind != "flat" && kind != "texture") throw new FormatException("Expected flat or texture after warp");
			var warp = new AnimdefsWarp { Name = Required().ToUpperInvariant(), IsTexture = kind == "texture", Style = style, Source = sourcename, Line = line };
			if(warp.Name.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH) throw new FormatException("Cannot use warp for long texture names");
			string token = Next();
			double speed;
			if(double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out speed) && !double.IsNaN(speed) && !double.IsInfinity(speed)) warp.Speed = speed;
			else if(token.Length > 0) Unread();
			Take("allowdecals");
			warps.Add(warp);
		}

		private void SkipDefinition()
		{
			for(string token = Next(); token.Length > 0; token = Next())
				if(topkeywords.Contains(token)) { Unread(); return; }
		}

		private void SkipSwitch()
		{
			string name = Required().ToLowerInvariant();
			if(name == "doom")
			{
				int number;
				string token = Next();
				if(!int.TryParse(token, out number)) Unread();
				Required();
			}
			else if(name == "heretic" || name == "hexen" || name == "strife" || name == "any") Required();
			for(string token = Next(); token.Length > 0; token = Next())
			{
				switch(token.ToLowerInvariant())
				{
					case "quest": case "on": case "off": break;
					case "sound": Required(); break;
					case "pic": Required(); SkipTime(); break;
					default: Unread(); return;
				}
			}
		}

		private void SkipDoor()
		{
			Required();
			for(string token = Next(); token.Length > 0; token = Next())
			{
				if(token.Equals("allowdecals", StringComparison.OrdinalIgnoreCase)) continue;
				if(token.Equals("pic", StringComparison.OrdinalIgnoreCase) || token.Equals("opensound", StringComparison.OrdinalIgnoreCase) || token.Equals("closesound", StringComparison.OrdinalIgnoreCase)) Required();
				else { Unread(); return; }
			}
		}

		private void SkipCanvas()
		{
			Required(); Required(); Required();
			for(string token = Next(); token.Length > 0; token = Next())
			{
				switch(token.ToLowerInvariant())
				{
					case "hdr": case "worldpanning": case "translucent": break;
					case "fit": Required(); Required(); break;
					case "offset":
						int number;
						string value = Next();
						if(int.TryParse(value, out number)) Required(); else if(value.Length > 0) Unread();
						break;
					default: Unread(); return;
				}
			}
		}

		private void SkipFire()
		{
			Required(); SkipTime(); Take("allowdecals");
			for(string token = Next(); token.Length > 0; token = Next())
			{
				if(token.Equals("color", StringComparison.OrdinalIgnoreCase)) { Required(); Required(); Required(); Required(); }
				else if(token.Equals("palette", StringComparison.OrdinalIgnoreCase)) Required();
				else { Unread(); return; }
			}
		}

		private void SkipTime()
		{
			string token = Required().ToLowerInvariant();
			if(token != "tics" && token != "rand") { Unread(); throw new FormatException("Expected tics or rand"); }
			Number();
			if(token == "rand") Number();
		}

		#endregion
	}
}
