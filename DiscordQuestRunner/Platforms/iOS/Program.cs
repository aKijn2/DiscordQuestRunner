using ObjCRuntime;
using UIKit;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Provides the native iOS process entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Starts the UIKit application loop with the MAUI app delegate.
        /// </summary>
        /// <param name="args">Process arguments supplied by iOS.</param>
        public static void Main(string[] args)
        {
            UIApplication.Main(args, null, typeof(AppDelegate));
        }
    }
}
