using System.Threading.Tasks;
using Vion.Cli.Infrastructure;

namespace Vion.Cli
{
    internal static class Program
    {
        private static Task<int> Main(string[] args)
        {
            return VionCli.RunAsync(args, CliServices.Real());
        }
    }
}