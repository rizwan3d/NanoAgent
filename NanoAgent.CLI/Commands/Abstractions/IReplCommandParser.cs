using NanoAgent.Application.Models;
using NanoAgent.CLI.Commands;

namespace NanoAgent.CLI.Commands;

public interface IReplCommandParser
{
    ParsedReplCommand Parse(string commandText);
}
