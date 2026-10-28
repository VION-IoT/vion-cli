using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     Every command's action runs inside this: a usage refusal exits 2, a failure exits 1, and in JSON mode either
    ///     is one `{ "error": … }` document on standard output, so that the mode has one stream whatever happened.
    /// </summary>
    internal static class Guard
    {
        public static async Task<int> RunAsync(GlobalValues globals, CliServices services, Func<Task<int>> body)
        {
            try
            {
                return await body();
            }
            catch (UsageException exception)
            {
                Report(globals, services, exception.Message, ExitCodes.Usage);
                return ExitCodes.Usage;
            }
            catch (CliFailure exception)
            {
                Report(globals, services, exception.Message, ExitCodes.Failure);
                return ExitCodes.Failure;
            }
        }

        public static void Report(GlobalValues globals, CliServices services, string message, int exitCode)
        {
            if (globals.Json)
            {
                var error = new JsonObject { ["error"] = new JsonObject { ["message"] = message, ["exitCode"] = exitCode } };
                Renderer.WriteJson(error, services);
            }
            else
            {
                services.Error.WriteLine($"error: {message}");
            }
        }
    }
}