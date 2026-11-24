namespace CodeImp.DoomBuilder.BuilderModes
{
	// Visual mode gravity, independent of the frame rate
	public static class CameraFall
	{
		// Fall acceleration per millisecond, in map units per reference tick
		public const float GRAVITY = -0.06f;

		// Falls move as they did at one step per this many ms (the Windows default timer rate)
		public const double REFERENCE_TICK_MS = 16.0;

		public static double Accelerate(double velocity, double gravityscale, long deltatime)
		{
			return velocity + GRAVITY * gravityscale * deltatime;
		}

		public static double Distance(double velocity, long deltatime)
		{
			return velocity * deltatime / REFERENCE_TICK_MS;
		}
	}
}
