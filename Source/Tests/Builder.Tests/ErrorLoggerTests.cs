using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class ErrorLoggerTests
	{
		[Theory]
		[InlineData(ErrorType.Warning)]
		[InlineData(ErrorType.Error)]
		public void AddDuplicateAndClear_WorkWithoutAMainWindow(ErrorType type)
		{
			Assert.Null(General.MainWindow);
			var logger = new ErrorLogger();
			logger.Add(type, "message");
			Assert.True(logger.HasErrors);
			Assert.True(logger.HasChanged);
			Assert.Equal(type == ErrorType.Error, logger.IsErrorAdded);
			Assert.Equal(type == ErrorType.Warning, logger.IsWarningAdded);

			logger.HasChanged = false;
			logger.Add(type, "message");
			Assert.False(logger.HasChanged);
			ErrorItem item = Assert.Single(logger.GetErrors(0));
			Assert.Equal(type, item.Type);
			Assert.Equal("message", item.Description);

			logger.Clear();
			Assert.Empty(logger.GetErrors(0));
			Assert.Equal(0, logger.ErrorsCount);
			Assert.False(logger.HasErrors);
			Assert.False(logger.HasChanged);
			Assert.False(logger.IsErrorAdded);
			Assert.False(logger.IsWarningAdded);
		}
	}
}
