using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

public interface IReplCommandParser
{
    ParsedReplCommand Parse(string commandText);
}
