using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

public interface IReplCommandParser
{
    ParsedReplCommand Parse(string commandText);
}
