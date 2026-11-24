#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.GZBuilder;
using CodeImp.DoomBuilder.VisualModes;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
	// Picks the dynamic lights touching each surface of a 3D render pass and fills the shader's light arrays.
	// Light boxes and uniform values are packed once per pass; a surface whose light set matches the previous one uploads nothing.
	internal sealed class SurfaceLights
	{
		internal const int MAX_PER_SURFACE = 64;

		// Per light of the pass: box centre and half size, then its uniform values
		private int count;
		private double[] centers = new double[0];
		private double[] halves = new double[0];
		private float[] colors = new float[0], positions = new float[0], orientations = new float[0];
		private float[] spotradii = new float[0], strengths = new float[0];

		// Lights of the current surface, and of the last upload (-1: nothing uploaded this pass)
		private readonly int[] selected = new int[MAX_PER_SURFACE];
		private int selectedcount;
		private readonly int[] uploaded = new int[MAX_PER_SURFACE];
		private int uploadedcount = -1;

		// Packed arrays handed to the device, one surface's lights followed by a terminator
		private readonly float[] upcolor = new float[MAX_PER_SURFACE * 4];
		private readonly float[] upposition = new float[MAX_PER_SURFACE * 4];
		private readonly float[] uporientation = new float[MAX_PER_SURFACE * 4];
		private readonly float[] upspotradius = new float[MAX_PER_SURFACE * 2];
		private readonly float[] upstrength = new float[MAX_PER_SURFACE * 2];

		internal int SelectedCount { get { return selectedcount; } }
		internal int Selected(int index) { return selected[index]; }

		// Starts a pass with these lights, in priority order
		internal void Begin(List<VisualThing> lights)
		{
			Begin(lights.Count);
			for(int i = 0; i < lights.Count; i++)
			{
				VisualThing light = lights[i];
				Vector4f orientation = new Vector4f();
				Vector2f spotradius = new Vector2f();
				if(light.LightType.LightType == GZGeneral.LightType.SPOT)
				{
					orientation = new Vector4f(light.VectorLookAt, 1f);
					spotradius = new Vector2f(CosDeg(light.LightSpotRadius1), CosDeg(light.LightSpotRadius2));
				}
				float diameter = light.LightRadius * 2;
				SetLight(i, light.BoundingBox, light.LightColor.ToVector(), new Vector4f(light.Center, light.LightRadius), orientation, spotradius,
					new Vector2f(Math.Min(1500.0f, (diameter * diameter) / 10), light.LightLinearity));
			}
		}

		internal void Begin(int lights)
		{
			count = lights;
			if(centers.Length < lights * 3)
			{
				centers = new double[lights * 3];
				halves = new double[lights * 3];
				colors = new float[lights * 4];
				positions = new float[lights * 4];
				orientations = new float[lights * 4];
				spotradii = new float[lights * 2];
				strengths = new float[lights * 2];
			}
			selectedcount = 0;
			uploadedcount = -1;
		}

		internal void SetLight(int index, Vector3D[] box, Vector4f color, Vector4f position, Vector4f orientation, Vector2f spotradius, Vector2f strength)
		{
			Vector3D half = box[0] - box[1];
			centers[index * 3] = box[0].x; centers[index * 3 + 1] = box[0].y; centers[index * 3 + 2] = box[0].z;
			halves[index * 3] = half.x; halves[index * 3 + 1] = half.y; halves[index * 3 + 2] = half.z;
			Put4(colors, index, color);
			Put4(positions, index, position);
			Put4(orientations, index, orientation);
			spotradii[index * 2] = spotradius.X; spotradii[index * 2 + 1] = spotradius.Y;
			strengths[index * 2] = strength.X; strengths[index * 2 + 1] = strength.Y;
		}

		// Selects the lights whose boxes touch this one (box[0] is the centre, box[1] a corner);
		// true when the device's light arrays need an upload for it
		internal bool Select(Vector3D[] box)
		{
			double cx = box[0].x, cy = box[0].y, cz = box[0].z;
			double hx = cx - box[1].x, hy = cy - box[1].y, hz = cz - box[1].z;
			int n = 0;
			for(int i = 0; i < count && n < MAX_PER_SURFACE; i++)
			{
				int k = i * 3;
				if(hx + halves[k] >= Math.Abs(cx - centers[k]) && hy + halves[k + 1] >= Math.Abs(cy - centers[k + 1])
					&& hz + halves[k + 2] >= Math.Abs(cz - centers[k + 2]))
					selected[n++] = i;
			}
			selectedcount = n;

			if(n != uploadedcount) return true;
			for(int i = 0; i < n; i++) if(selected[i] != uploaded[i]) return true;
			return false;
		}

		// Uploads the current selection: colours with a zero-alpha terminator, the rest only when there are lights
		internal void Upload(RenderDevice graphics)
		{
			int n = selectedcount;
			graphics.SetUniformArray(UniformName.lightColor, upcolor, Pack(), 4);
			if(n == 0) return;
			graphics.SetUniformArray(UniformName.lightPosAndRadius, upposition, n, 4);
			graphics.SetUniformArray(UniformName.lightOrientation, uporientation, n, 4);
			graphics.SetUniformArray(UniformName.light2Radius, upspotradius, n, 2);
			graphics.SetUniformArray(UniformName.lightStrengthAndLinearity, upstrength, n, 2);
		}

		// Fills the packed arrays from the selection and records it as uploaded; returns the colour element count
		internal int Pack()
		{
			int n = selectedcount;
			for(int i = 0; i < n; i++)
			{
				int light = selected[i];
				Array.Copy(colors, light * 4, upcolor, i * 4, 4);
				Array.Copy(positions, light * 4, upposition, i * 4, 4);
				Array.Copy(orientations, light * 4, uporientation, i * 4, 4);
				Array.Copy(spotradii, light * 2, upspotradius, i * 2, 2);
				Array.Copy(strengths, light * 2, upstrength, i * 2, 2);
				uploaded[i] = light;
			}
			uploadedcount = n;

			if(n == MAX_PER_SURFACE) return n;
			Array.Clear(upcolor, n * 4, 4);
			return n + 1;
		}

		// Packed arrays as the last Pack left them
		internal float[] PackedColors { get { return upcolor; } }
		internal float[] PackedPositions { get { return upposition; } }
		internal float[] PackedStrengths { get { return upstrength; } }

		private static void Put4(float[] dest, int index, Vector4f v)
		{
			dest[index * 4] = v.X; dest[index * 4 + 1] = v.Y; dest[index * 4 + 2] = v.Z; dest[index * 4 + 3] = v.W;
		}

		internal static float CosDeg(float angle)
		{
			return (float)Math.Cos(Angle2D.DegToRad(angle));
		}
	}
}
