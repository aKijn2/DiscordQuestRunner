using ObjCRuntime;
using UIKit;

namespace DiscordQuestRunner
{
    /// <summary>
    /// Provides the native Mac Catalyst process entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Starts the UIKit application loop with the MAUI app delegate.
        /// </summary>
        /// <param name="args">Process arguments supplied by the host environment.</param>
        public static void Main(string[] args)
        {
            UIApplication.Main(args, null, typeof(AppDelegate));
        }
    }
}
