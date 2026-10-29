using System;

namespace Vion.Cli.Infrastructure
{
    internal static class ExitCodes
    {
        public const int Ok = 0;

        /// <summary>A call or a sign-in failed.</summary>
        public const int Failure = 1;

        /// <summary>The invocation was wrong: a parse error, a missing value, a refused flag.</summary>
        public const int Usage = 2;
    }

    /// <summary>The invocation cannot be sent as written; the message names what to pass. Exit 2.</summary>
    internal sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message)
        {
        }
    }

    /// <summary>A call or a sign-in failed; the message is the whole story in table mode. Exit 1.</summary>
    internal sealed class CliFailure : Exception
    {
        public CliFailure(string message) : base(message)
        {
        }
    }
}